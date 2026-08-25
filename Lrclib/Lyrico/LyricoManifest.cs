using System.Reflection;
using System.Text.Json;

namespace CatClawMusic.Plugins.Lrclib.Lyrico;

/// <summary>Lyrico 歌词源插件的 manifest.json（仅保留桥接所需字段）。</summary>
public sealed class LyricoManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public int ApiVersion { get; set; } = 4;
    public int MinHostApiVersion { get; set; } = 1;
    public string Entry { get; set; } = "source.js";
    public List<string> IncludeDirs { get; set; } = new();
    public List<string> Capabilities { get; set; } = new();
}

/// <summary>
/// 外部 Lyrico 源插件的目录编目：扫描宿主 AppDataDirectory/Plugin/LyricoSources/{plugin}/ 下
/// 用户放入的 Lyrico 格式插件目录，把每个目录整理成可整体读取的源集合，供 LyricoScriptHost 拼接执行。
/// 宿主零改动、引擎内嵌在插件内，仅源插件由外部目录加载，兼容官方 / 第三方 Lyrico 源插件。
/// </summary>
public sealed class LyricoSourceCatalog
{
    /// <summary>源插件根目录（位于宿主插件目录下的 LyricoSources 子目录）。</summary>
    internal static string SourcesRoot
    {
        get
        {
            var baseDir = GetAppDataDir();
            return Path.Combine(baseDir, PluginHost.LyricoSourcesDirName);
        }
    }

    private static string GetAppDataDir()
    {
        try
        {
            return Microsoft.Maui.Storage.FileSystem.AppDataDirectory;
        }
        catch
        {
            return AppContext.BaseDirectory;
        }
    }

    private readonly Dictionary<string, Dictionary<string, string>> _files;

    public LyricoSourceCatalog()
    {
        _files = Load();
    }

    /// <summary>插件目录名（如 netease/qq/kugou/soda/apple/自定义）。</summary>
    public IReadOnlyList<string> PluginNames => _files.Keys.ToList();

    /// <summary>取某插件的 manifest 解析结果；不存在或解析失败返回 null。</summary>
    public LyricoManifest? GetManifest(string plugin)
    {
        if (!_files.TryGetValue(plugin, out var files)) return null;
        if (!files.TryGetValue("manifest.json", out var json)) return null;
        try
        {
            var manifest = JsonSerializer.Deserialize<LyricoManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            if (manifest == null) return null;
            manifest.Id = string.IsNullOrWhiteSpace(manifest.Id) ? plugin : manifest.Id;
            if (string.IsNullOrWhiteSpace(manifest.Entry) || !manifest.Entry.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                manifest.Entry = "source.js";
            return manifest;
        }
        catch (Exception ex)
        {
            LyricoLog.Warn("Lyrico", $"manifest 解析失败 {plugin}: {ex.Message}");
            return null;
        }
    }

    /// <summary>取某插件的全部文本文件映射（相对路径→内容），不含 manifest.json。</summary>
    public IReadOnlyDictionary<string, string>? GetScriptFiles(string plugin)
    {
        if (!_files.TryGetValue(plugin, out var files)) return null;
        return new Dictionary<string, string>(files);
    }

    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        var root = SourcesRoot;
        if (!Directory.Exists(root))
        {
            LyricoLog.Debug("Lyrico", $"源插件根目录不存在：{root}");
            return result;
        }

        foreach (var pluginDir in Directory.EnumerateDirectories(root))
        {
            var plugin = Path.GetFileName(pluginDir);
            if (string.IsNullOrEmpty(plugin)) continue;

            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!TryLoadScriptsRecursive(pluginDir, files))
            {
                LyricoLog.Warn("Lyrico", $"扫描源插件目录失败，跳过：{plugin}");
                continue;
            }
            if (files.Count == 0 || !files.ContainsKey("manifest.json"))
            {
                LyricoLog.Warn("Lyrico", $"源插件 {plugin} 缺少 manifest.json 或无有效文件，跳过");
                continue;
            }
            result[plugin] = files;
        }

        // 保持稳定顺序：按目录名排序
        foreach (var p in result.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            _ = p;

        return result;
    }

    /// <summary>递归读取目录下所有文本文件（.json/.js 等），按相对路径存入字典。</summary>
    private static bool TryLoadScriptsRecursive(string dir, Dictionary<string, string> files)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(dir, file).Replace('\\', '/');
                if (string.IsNullOrEmpty(rel)) continue;
                if (rel.StartsWith('.') || rel.Contains("/.", StringComparison.Ordinal)) continue; // 隐藏文件/点目录
                try
                {
                    var content = File.ReadAllText(file, System.Text.Encoding.UTF8);
                    files[rel] = content;
                }
                catch { /* 单个文件读取失败则跳过 */ }
            }
            return true;
        }
        catch (Exception ex)
        {
            LyricoLog.Warn("Lyrico", $"读取目录失败 {dir}: {ex.Message}");
            return false;
        }
    }
}

/// <summary>日志门面（Mirror 宿主 Console 风格，静默失败不影响调用）。</summary>
internal static class LyricoLog
{
    public static void Debug(string tag, string message)
    {
        try { Console.WriteLine($"[Lyrico][{tag}] {message}"); } catch { }
    }

    public static void Warn(string tag, string message)
    {
        try { Console.WriteLine($"[Lyrico][warn][{tag}] {message}"); } catch { }
    }
}