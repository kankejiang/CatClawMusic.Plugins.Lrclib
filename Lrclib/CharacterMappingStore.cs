using System.Text.Json;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 重命名字符映射规则存储（Lyrico <c>CharacterMappingRule</c> + <c>CharacterMappingDefaults</c>）：
/// 持久化 from→to 字符映射，批量重命名时替换非法字符。默认把文件名非法符映射为全角等价符。
/// <para>文件：{LocalApplicationData}/CatClawMusic.Maui/character_mapping.json，mtime 热重载。</para>
/// </summary>
public class CharacterMappingStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CatClawMusic.Maui", "character_mapping.json");

    /// <summary>默认映射：Windows/Linux 文件名非法符 → 全角等价符（同 Lyrico DEFAULT_INVALID_CHARS）。</summary>
    public static readonly (char From, char To)[] DefaultMappings =
    {
        ('\\', '＼'), ('/', '／'), (':', '：'), ('*', '＊'), ('?', '？'),
        ('"', '＂'), ('<', '＜'), ('>', '＞'), ('|', '｜'),
    };

    private readonly object _lock = new();
    private List<(char From, char To)> _mappings = new(DefaultMappings);
    private DateTime _lastWriteUtc = DateTime.MinValue;

    public CharacterMappingStore() => RefreshIfChangedLocked();

    /// <summary>取映射查找表（from→to）。空 from 跳过。</summary>
    public Dictionary<char, char> GetMapping()
    {
        lock (_lock)
        {
            RefreshIfChangedLocked();
            var dict = new Dictionary<char, char>();
            foreach (var (from, to) in _mappings)
                if (from != '\0') dict[from] = to;
            return dict;
        }
    }

    /// <summary>取映射列表（UI 展示用）。</summary>
    public List<(char From, char To)> GetMappings()
    {
        lock (_lock) { RefreshIfChangedLocked(); return _mappings.ToList(); }
    }

    /// <summary>保存映射列表（去重 by From）+ 持久化。</summary>
    public void Save(IEnumerable<(char From, char To)> mappings)
    {
        lock (_lock)
        {
            _mappings = mappings.Where(m => m.From != '\0')
                .GroupBy(m => m.From).Select(g => g.First()).ToList();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var arr = _mappings.Select(m => new { from = m.From, to = m.To });
                File.WriteAllText(FilePath, JsonSerializer.Serialize(arr, new JsonSerializerOptions { WriteIndented = true }));
                _lastWriteUtc = File.GetLastWriteTimeUtc(FilePath);
            }
            catch { }
        }
    }

    /// <summary>恢复默认映射。</summary>
    public void ResetToDefaults() => Save(DefaultMappings);

    private void RefreshIfChangedLocked()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var mtime = File.GetLastWriteTimeUtc(FilePath);
            if (mtime == _lastWriteUtc) return;
            _lastWriteUtc = mtime;
            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
            var list = new List<(char, char)>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var from = e.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.String && f.GetString()!.Length > 0 ? f.GetString()![0] : '\0';
                var to = e.TryGetProperty("to", out var t) && t.ValueKind == JsonValueKind.String && t.GetString()!.Length > 0 ? t.GetString()![0] : '\0';
                if (from != '\0') list.Add((from, to));
            }
            _mappings = list.Count > 0 ? list : new List<(char, char)>(DefaultMappings);
        }
        catch { }
    }
}
