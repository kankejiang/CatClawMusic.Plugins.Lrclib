using System.Text.Json;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 艺术家拆分配置存储（复刻 Lyrico <c>SettingsRepository</c> 的 artistSplitConfig 持久化）：
/// 把 <see cref="ArtistSplitConfig"/> 持久化到本地 JSON。
/// <para>文件位置：{LocalApplicationData}/CatClawMusic.Maui/artist_split_config.json。</para>
/// <para>多实例一致性：读操作前按文件修改时间热重载。</para>
/// </summary>
public class ArtistSplitStore
{
    /// <summary>任意实例的配置被保存/重置时触发（艺人库据此重建艺人索引）</summary>
    public static event Action? AnyChanged;

    private static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CatClawMusic.Maui", "artist_split_config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly object _lock = new();
    private ArtistSplitConfig _config = new();
    private DateTime _lastWriteUtc = DateTime.MinValue;

    public ArtistSplitStore()
    {
        lock (_lock) RefreshIfChangedLocked();
    }

    /// <summary>读取当前配置（热重载后的最新值）</summary>
    public ArtistSplitConfig Get()
    {
        lock (_lock)
        {
            RefreshIfChangedLocked();
            return _config;
        }
    }

    /// <summary>整体替换配置并持久化</summary>
    public void Save(ArtistSplitConfig config)
    {
        lock (_lock)
        {
            _config = config;
            SaveLocked();
        }
        AnyChanged?.Invoke();
    }

    /// <summary>重置为全新默认配置（清空所有覆盖与自定义项，保留默认开启状态）</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _config = new ArtistSplitConfig();
            SaveLocked();
        }
        AnyChanged?.Invoke();
    }

    private void SaveLocked()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_config, JsonOptions));
            _lastWriteUtc = File.GetLastWriteTimeUtc(FilePath);
        }
        catch
        {
            // 持久化失败不阻断编辑会话
        }
    }

    private void RefreshIfChangedLocked()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                _config = new ArtistSplitConfig();
                _lastWriteUtc = DateTime.MinValue;
                return;
            }

            var mtime = File.GetLastWriteTimeUtc(FilePath);
            if (mtime > _lastWriteUtc)
            {
                try
                {
                    var loaded = JsonSerializer.Deserialize<ArtistSplitConfig>(File.ReadAllText(FilePath), JsonOptions);
                    _config = loaded ?? new ArtistSplitConfig();
                }
                catch
                {
                    // 文件损坏时保留当前内存值
                }
                _lastWriteUtc = mtime;
            }
        }
        catch
        {
            // 读取失败保留当前值
        }
    }
}