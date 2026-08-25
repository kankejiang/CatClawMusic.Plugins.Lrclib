using System.Text.Json;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 歌词清理规则存储（Lyrico <c>LyricsCleanupRulesScreen</c> 配置）：
/// 持久化默认的标签行过滤关键词 + 去空行开关，供 SearchLyrics/BatchLyricsFormat 复用。
/// <para>文件：{LocalApplicationData}/CatClawMusic.Maui/lrclib_cleanup_rules.json，mtime 热重载。</para>
/// </summary>
public class LyricCleanupRulesStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CatClawMusic.Maui", "lrclib_cleanup_rules.json");

    /// <summary>默认标签行过滤关键词（LRC 元数据标签头）。</summary>
    public static readonly string[] DefaultTagKeywords =
        { "[ti:", "[ar:", "[al:", "[by:", "[re:", "[ve:", "[offset:" };

    private readonly object _lock = new();
    private List<string> _tagKeywords = new(DefaultTagKeywords);
    private bool _removeEmptyLines = true;
    private DateTime _lastWriteUtc = DateTime.MinValue;

    public LyricCleanupRulesStore() => RefreshIfChangedLocked();

    /// <summary>标签行过滤关键词（去除空项后返回新列表）。</summary>
    public List<string> GetTagKeywords()
    {
        lock (_lock) { RefreshIfChangedLocked(); return _tagKeywords.Where(k => k.Length > 0).ToList(); }
    }

    /// <summary>默认去空行开关。</summary>
    public bool GetRemoveEmptyLinesDefault()
    {
        lock (_lock) { RefreshIfChangedLocked(); return _removeEmptyLines; }
    }

    /// <summary>保存（立即持久化 + 更新内存）。</summary>
    public void Save(IEnumerable<string> tagKeywords, bool removeEmptyLinesDefault)
    {
        lock (_lock)
        {
            _tagKeywords = tagKeywords.Select(k => k.Trim()).Where(k => k.Length > 0).Distinct().ToList();
            _removeEmptyLines = removeEmptyLinesDefault;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var payload = new { tagKeywords = _tagKeywords, removeEmptyLines = _removeEmptyLines };
                File.WriteAllText(FilePath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
                _lastWriteUtc = File.GetLastWriteTimeUtc(FilePath);
            }
            catch { }
        }
    }

    /// <summary>恢复默认关键词 + 去空行开。</summary>
    public void ResetToDefaults()
    {
        Save(DefaultTagKeywords, true);
    }

    private void RefreshIfChangedLocked()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var mtime = File.GetLastWriteTimeUtc(FilePath);
            if (mtime == _lastWriteUtc) return;
            _lastWriteUtc = mtime;
            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
            var root = doc.RootElement;
            if (root.TryGetProperty("tagKeywords", out var kws) && kws.ValueKind == JsonValueKind.Array)
                _tagKeywords = kws.EnumerateArray().Select(e => e.GetString() ?? "").ToList();
            if (root.TryGetProperty("removeEmptyLines", out var rel) && rel.ValueKind == JsonValueKind.False)
                _removeEmptyLines = false;
            else if (root.TryGetProperty("removeEmptyLines", out var relt) && relt.ValueKind == JsonValueKind.True)
                _removeEmptyLines = true;
        }
        catch { }
    }
}
