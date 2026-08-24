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
/// 内嵌 Lyrico 源插件的目录编目：把打包进程序集资源的“Lyrico.Sources.{plugin}\...”文件
/// 按插件目录整理成可整体读取的源集合，供 LyricoScriptHost 拼接执行。
/// </summary>
public sealed class LyricoSourceCatalog
{
    /// <summary>插件资源前缀：各文件 LogicalName = "Lyrico.Sources.{plugin目录}\{相对路径}"。</summary>
    private const string ResourcePrefix = "Lyrico.Sources.";
    private static readonly string[] KnownPlugins = { "netease", "qq", "kugou", "soda", "apple" };

    private readonly Dictionary<string, Dictionary<string, string>> _files;

    public LyricoSourceCatalog()
    {
        _files = Load();
    }

    /// <summary>插件目录名（netease/qq/kugou/soda/apple）。</summary>
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
        var asm = typeof(LyricoSourceCatalog).Assembly;
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase)) continue;
            // "Lyrico.Sources.{plugin}\{相对路径}" 拆分出插件目录名
            var relative = name.Substring(ResourcePrefix.Length);
            var sep = relative.IndexOf('\\');
            if (sep <= 0) continue;
            var plugin = relative.Substring(0, sep);
            var filePath = relative.Substring(sep + 1).Replace('\\', '/');
            if (string.IsNullOrEmpty(filePath)) continue;

            string content;
            try
            {
                using var stream = asm.GetManifestResourceStream(name);
                if (stream == null) continue;
                using var reader = new StreamReader(stream);
                content = reader.ReadToEnd();
            }
            catch
            {
                continue;
            }

            if (!result.TryGetValue(plugin, out var files))
            {
                files = new Dictionary<string, string>(StringComparer.Ordinal);
                result[plugin] = files;
            }
            files[filePath] = content;
        }

        // 保持稳定顺序：KnownPlugins 优先，其余按名排序
        var ordered = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in KnownPlugins)
            if (result.TryGetValue(p, out var f)) ordered[p] = f;
        foreach (var p in result.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            if (!ordered.ContainsKey(p)) ordered[p] = result[p];
        return ordered;
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