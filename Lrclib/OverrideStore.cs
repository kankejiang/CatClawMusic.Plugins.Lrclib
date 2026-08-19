using System.Text.Json;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 手动匹配覆盖记录存储：用户在"歌词匹配"入口页选定的 LRCLIB 曲目
/// 按 歌名|艺人 持久化到本地 JSON，之后播放命中即优先返回，不再走自动匹配。
/// <para>文件位置：{LocalApplicationData}/CatClawMusic.Maui/lrclib_overrides.json
/// （与网易云插件 cookie 文件同目录约定）。</para>
/// <para>多实例一致性：读操作前按文件修改时间热重载，任意实例的写入
/// （含外部编辑文件）对其他实例立即可见。</para>
/// </summary>
public class OverrideStore
{
    private static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CatClawMusic.Maui", "lrclib_overrides.json");

    private readonly object _lock = new();
    private Dictionary<string, LrclibTrack> _overrides;
    private DateTime _lastWriteUtc = DateTime.MinValue;

    public OverrideStore()
    {
        _overrides = new Dictionary<string, LrclibTrack>();
        RefreshIfChangedLocked();
    }

    /// <summary>规范化覆盖键：歌名|艺人（小写去首尾空白），同一首歌的覆盖与歌曲来源无关</summary>
    public static string NormalizeKey(string title, string? artist)
        => $"{title.Trim().ToLowerInvariant()}|{(artist ?? string.Empty).Trim().ToLowerInvariant()}";

    /// <summary>获取覆盖记录；无则返回 null</summary>
    public LrclibTrack? Get(string title, string? artist)
    {
        lock (_lock)
        {
            RefreshIfChangedLocked();
            return _overrides.TryGetValue(NormalizeKey(title, artist), out var track) ? track : null;
        }
    }

    /// <summary>写入覆盖记录并持久化</summary>
    public void Set(string title, string? artist, LrclibTrack track)
    {
        lock (_lock)
        {
            RefreshIfChangedLocked();
            _overrides[NormalizeKey(title, artist)] = track;
            SaveLocked();
        }
    }

    /// <summary>删除覆盖记录并持久化</summary>
    public void Remove(string title, string? artist)
    {
        RemoveKey(NormalizeKey(title, artist));
    }

    /// <summary>按规范化键删除覆盖记录并持久化（键来源：<see cref="GetAll"/> 或 <see cref="NormalizeKey"/>）</summary>
    public void RemoveKey(string key)
    {
        lock (_lock)
        {
            RefreshIfChangedLocked();
            if (_overrides.Remove(key))
                SaveLocked();
        }
    }

    /// <summary>全部覆盖记录（用于管理页展示；按键排序保证顺序稳定）</summary>
    public List<(string Key, string Title, string Artist, LrclibTrack Track)> GetAll()
    {
        lock (_lock)
        {
            RefreshIfChangedLocked();
            var list = _overrides
                .Select(kv =>
                {
                    // 键格式为 "title|artist"，展示时拆开
                    var sep = kv.Key.IndexOf('|');
                    var title = sep >= 0 ? kv.Key[..sep] : kv.Key;
                    var artist = sep >= 0 ? kv.Key[(sep + 1)..] : string.Empty;
                    return (kv.Key, Title: title, Artist: artist, kv.Value);
                })
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .ToList();
            return list;
        }
    }

    /// <summary>文件被其他实例（或外部编辑）改动时重载；无改动则零开销</summary>
    private void RefreshIfChangedLocked()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                if (_overrides.Count > 0)
                {
                    _overrides = new Dictionary<string, LrclibTrack>();
                    _lastWriteUtc = DateTime.MinValue;
                }
                return;
            }

            var mtime = File.GetLastWriteTimeUtc(FilePath);
            if (mtime > _lastWriteUtc)
            {
                _overrides = Load();
                _lastWriteUtc = mtime;
            }
        }
        catch { }
    }

    private static Dictionary<string, LrclibTrack> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var list = JsonSerializer.Deserialize<List<OverrideEntry>>(json);
                if (list != null)
                {
                    var dict = new Dictionary<string, LrclibTrack>();
                    foreach (var e in list)
                    {
                        if (!string.IsNullOrWhiteSpace(e.Key) && e.Track != null)
                            dict[e.Key] = e.Track;
                    }
                    return dict;
                }
            }
        }
        catch { }
        return new Dictionary<string, LrclibTrack>();
    }

    private void SaveLocked()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(
                _overrides.Select(kv => new OverrideEntry { Key = kv.Key, Track = kv.Value }));
            File.WriteAllText(FilePath, json);
            _lastWriteUtc = File.GetLastWriteTimeUtc(FilePath);
        }
        catch { }
    }

    /// <summary>覆盖记录条目（序列化结构）</summary>
    private class OverrideEntry
    {
        public string Key { get; set; } = string.Empty;
        public LrclibTrack Track { get; set; } = new();
    }
}
