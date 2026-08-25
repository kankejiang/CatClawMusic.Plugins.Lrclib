using System.IO.Compression;
using System.Text.Json;
using Microsoft.Maui.Storage;

namespace CatClawMusic.Plugins.Lrclib.Lyrico;

/// <summary>
    /// Lyrico 源插件导入器：把用户选择的 .zip 包解包到
    /// <see cref="LyricoSourceCatalog.SourcesRoot"/>/{pluginId}/，校验 manifest.json 存在且声明
    /// getLyrics 能力，成功后调用 <see cref="LyricoLyricsHub.Refresh"/> 重建内存源集合。
    /// <para>兼容两种 zip 布局：扁平（manifest.json 在 zip 根）与包裹（pluginname/manifest.json）。
    /// 也支持多源 zip：zip 内多个子目录各含 manifest.json 时，逐目录安装为多个源。</para>
    /// <para>包格式参考 Lyrico 官方 SourcePluginInstaller：ZIP 内含 manifest.json + entry(.js) + 可选 includeDirs。</para>
    /// </summary>
public static class LyricoSourceInstaller
{
    /// <summary>单个插件包解压后最大体积（防 zip 炸弹）。</summary>
    private const long MaxUncompressedBytes = 5L * 1024 * 1024;
    /// <summary>manifest.json 最大体积。</summary>
    private const int MaxManifestBytes = 128 * 1024;
    /// <summary>单个 .js 最大体积。</summary>
    private const int MaxScriptBytes = 1024 * 1024;
    /// <summary>zip 内最多文件数。</summary>
    private const int MaxFileCount = 1000;

    /// <summary>导入结果。</summary>
    public sealed class ImportResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";
        public string? PluginId { get; init; }
        public string? PluginName { get; init; }
    }

    /// <summary>从指定 .zip 文件导入 Lyrico 源插件，成功后刷新 hub。</summary>
    public static async Task<ImportResult> ImportAsync(string zipPath, LyricoLyricsHub hub, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            return new ImportResult { Message = "插件文件不存在" };

        var root = LyricoSourceCatalog.SourcesRoot;
        try { Directory.CreateDirectory(root); } catch { }

        var tempDir = Path.Combine(root, ".import-" + Guid.NewGuid().ToString("N"));
        try
        {
            var files = new List<(string ZipPath, string DiskRelPath)>();

            await Task.Run(() =>
            {
                using var fs = File.OpenRead(zipPath);
                using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
                if (zip.Entries.Count > MaxFileCount)
                    throw new InvalidOperationException($"zip 内文件过多（>{MaxFileCount}）");

                long total = 0;
                foreach (var entry in zip.Entries)
                {
                    if (entry.Length > MaxScriptBytes)
                        throw new InvalidOperationException($"文件过大：{entry.FullName}（{entry.Length} 字节）");
                    total += entry.Length;
                    if (total > MaxUncompressedBytes)
                        throw new InvalidOperationException("解压后体积超限（>5MB）");
                }

                Directory.CreateDirectory(tempDir);
                foreach (var entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;  // 目录项
                    var rel = entry.FullName.Replace('\\', '/');
                    // 安全：禁止路径穿越
                    if (rel.Contains("..", StringComparison.Ordinal)) continue;
                    var diskPath = Path.Combine(tempDir, rel.Replace('/', Path.DirectorySeparatorChar));
                    var dir = Path.GetDirectoryName(diskPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    using var es = entry.Open();
                    using var outFs = File.Create(diskPath);
                    es.CopyTo(outFs);
                    files.Add((rel, rel));
                }
            }, ct).ConfigureAwait(false);

            // 找所有 manifest.json：优先 zip 根，其次任意子目录。每个含 manifest 的
            // 目录（Lyrico 多源 zip 的 pluginRoot）视为一个独立源插件逐目录安装。
            var manifestCandidates = files
                .Select(f => f.ZipPath)
                .Where(p => p.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Count(c => c == '/'))  // 越浅越优先
                .ThenBy(p => p)
                .ToList();

            if (manifestCandidates.Count == 0)
                return new ImportResult { Message = "包内未找到 manifest.json" };

            // 每个 manifest 所在目录即为一个插件根；zip 根上的 manifest 视为扁平单源
            var pluginRoots = manifestCandidates
                .Select(GetPluginRoot)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var installed = new List<string>();
            var failed = new List<string>();
            string? firstInstalledId = null;

            foreach (var pluginRoot in pluginRoots)
            {
                // 该插件根下最浅的那个 manifest
                var manifestPath = manifestCandidates
                    .Where(p => string.Equals(GetPluginRoot(p), pluginRoot, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p.Count(c => c == '/'))
                    .First();

                var manifestDiskPath = Path.Combine(tempDir, manifestPath.Replace('/', Path.DirectorySeparatorChar));
                var manifestBytes = File.ReadAllBytes(manifestDiskPath);
                if (manifestBytes.Length > MaxManifestBytes)
                {
                    failed.Add($"{manifestPath}：manifest.json 体积超限");
                    continue;
                }

                var manifest = ParseManifest(manifestBytes);
                if (manifest == null)
                {
                    failed.Add($"{manifestPath}：manifest.json 解析失败");
                    continue;
                }

                var pluginId = string.IsNullOrWhiteSpace(manifest.Id)
                    ? Path.GetFileNameWithoutExtension(zipPath)
                    : SanitizeDirName(manifest.Id);

                // 校验能力：只接受声明 getLyrics 的源（本插件用途是歌词兜底）
                var caps = manifest.Capabilities ?? new List<string>();
                if (!caps.Contains("getLyrics", StringComparer.OrdinalIgnoreCase))
                {
                    failed.Add($"「{manifest.Name}」未声明 getLyrics 能力（声明：{string.Join("/", caps)}）");
                    continue;
                }

                // 校验 entry 文件存在
                var entryRel = string.IsNullOrWhiteSpace(manifest.Entry) ? "source.js" : manifest.Entry;
                var entryZipPath = string.IsNullOrEmpty(pluginRoot) ? entryRel : $"{pluginRoot}/{entryRel}";
                if (!files.Any(f => string.Equals(f.ZipPath, entryZipPath, StringComparison.OrdinalIgnoreCase)))
                {
                    failed.Add($"「{manifest.Name}」入口脚本 {entryRel} 在包内不存在");
                    continue;
                }

                // 装入目标目录：LyricoSources/{pluginId}/
                var destDir = Path.Combine(root, pluginId);
                if (Directory.Exists(destDir))
                {
                    // 版本冲突检测：已安装版本更高时拒绝降级（对齐 Lyrico SourcePluginInstaller DOWNGRADE 拒绝）。
                    var existingManifestPath = Path.Combine(destDir, "manifest.json");
                    if (File.Exists(existingManifestPath))
                    {
                        var existing = ParseManifest(File.ReadAllBytes(existingManifestPath));
                        if (existing != null && existing.VersionCode > manifest.VersionCode)
                        {
                            failed.Add($"「{manifest.Name}」降级被拒：已安装 v{existing.VersionName}，" +
                                       $"导入包为 v{manifest.VersionName}。先卸载旧版再导入。");
                            continue;
                        }
                    }
                    Directory.Delete(destDir, recursive: true);  // 同版本或升级：覆盖旧版
                }
                Directory.CreateDirectory(destDir);

                // 只拷贝该插件根下的文件（去掉外层包裹目录）
                foreach (var f in files)
                {
                    var relFromRoot = string.IsNullOrEmpty(pluginRoot)
                        ? f.ZipPath
                        : (f.ZipPath.StartsWith(pluginRoot + "/", StringComparison.OrdinalIgnoreCase)
                            ? f.ZipPath[(pluginRoot.Length + 1)..]
                            : null);
                    if (string.IsNullOrEmpty(relFromRoot)) continue;
                    // 跳过 manifest.json 本身外的非脚本文件也允许（lib 目录等）
                    var src = Path.Combine(tempDir, f.ZipPath.Replace('/', Path.DirectorySeparatorChar));
                    var dst = Path.Combine(destDir, relFromRoot.Replace('/', Path.DirectorySeparatorChar));
                    var dstParent = Path.GetDirectoryName(dst);
                    if (!string.IsNullOrEmpty(dstParent)) Directory.CreateDirectory(dstParent);
                    File.Copy(src, dst, overwrite: true);
                }

                installed.Add($"{pluginId}");
                firstInstalledId ??= pluginId;
            }

            if (installed.Count > 0) hub.Refresh();

            if (installed.Count == 0)
                return new ImportResult
                {
                    Message = failed.Count > 0 ? $"没有源导入成功：{string.Join("；", failed)}" : "包内无可导入的源",
                };

            var ok = $"已导入 {installed.Count} 个源：{string.Join("、", installed)}";
            if (failed.Count > 0) ok += $"；未导入 {failed.Count} 个（{string.Join("；", failed)}）";
            return new ImportResult
            {
                Success = true,
                Message = ok,
                PluginId = firstInstalledId,
            };
        }
        catch (Exception ex)
        {
            return new ImportResult { Message = "导入失败：" + ex.Message };
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>解析 manifest（仅取桥接所需字段，忽略未知键）。</summary>
    private static LyricoManifest? ParseManifest(byte[] bytes)
    {
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<LyricoManifest>(bytes, opts);
        }
        catch { return null; }
    }

    /// <summary>把插件 id 规范化为安全目录名（去路径分隔符、非法字符）。</summary>
    private static string SanitizeDirName(string id)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) id = id.Replace(c, '_');
        return id.Trim().Trim('.');
    }

    /// <summary>取 zip 内路径的所属插件根（manifest 所在目录前缀；空串表示 zip 根）。</summary>
    private static string GetPluginRoot(string zipPath)
    {
        var idx = zipPath.LastIndexOf('/');
        return idx <= 0 ? "" : zipPath[..idx];
    }
}
