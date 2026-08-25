using System.Text.Json;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 编辑设置持久化数据（对应 Lyrico <c>EditFieldVisibilityOverridesJson</c> + <c>CustomTagSettings</c>）：
/// 可见性覆盖（分组/字段 → 是否可见）+ 自定义标签可见键列表。
/// </summary>
public sealed class EditorSettings
{
    public int Version { get; set; } = 1;

    /// <summary>编辑字段可见性覆盖：分组代码/字段代码 → 是否可见（未记录的用默认值）</summary>
    public Dictionary<string, bool> FieldOverrides { get; set; } = new();

    /// <summary>自定义标签可见键（大写规范化，去重有序）</summary>
    public List<string> CustomVisibleKeys { get; set; } = new();
}

/// <summary>
/// 编辑设置存储（复刻 Lyrico 的 DataStore 仓库）：
/// 把 字段可见性 + 自定义标签可见键 持久化到本地 JSON。
/// <para>文件位置：{LocalApplicationData}/CatClawMusic.Maui/editor_settings.json
/// （与 lrclib_overrides.json 同目录约定，卸载应用后保留备份仍可访问）。</para>
/// <para>多实例一致性：读操作前按文件修改时间热重载，任意实例的改动对其他实例立即可见。</para>
/// </summary>
public class EditorSettingsStore
{
    private static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CatClawMusic.Maui", "editor_settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly object _lock = new();
    private EditorSettings _settings = new();
    private DateTime _lastWriteUtc = DateTime.MinValue;

    public EditorSettingsStore()
    {
        lock (_lock) RefreshIfChangedLocked();
    }

    /// <summary>读取当前设置（热重载后的最新值）</summary>
    public EditorSettings Get()
    {
        lock (_lock)
        {
            RefreshIfChangedLocked();
            return _settings;
        }
    }

    /// <summary>整体替换设置并持久化（setVisibleKeys 用）</summary>
    public void Save(EditorSettings settings)
    {
        lock (_lock)
        {
            _settings = settings;
            SaveLocked();
        }
    }

    /// <summary>写入单个字段可见性覆盖（分组或字段代码）并持久化</summary>
    public void SetFieldOverride(string code, bool visible)
    {
        lock (_lock)
        {
            RefreshIfChangedLocked();
            _settings.FieldOverrides[code] = visible;
            SaveLocked();
        }
    }

    /// <summary>重置所有可见性覆盖为默认值（清空覆盖映射）</summary>
    public void ResetFieldOverrides()
    {
        lock (_lock)
        {
            RefreshIfChangedLocked();
            _settings.FieldOverrides.Clear();
            SaveLocked();
        }
    }

    /// <summary>新增自定义标签可见键（大写规范化；非法/重复忽略）。返回 null 表示输入非法。</summary>
    public string? AddCustomVisibleKey(string input)
    {
        var key = NormalizeKey(input);
        if (key == null) return null;

        lock (_lock)
        {
            RefreshIfChangedLocked();
            if (!_settings.CustomVisibleKeys.Contains(key))
            {
                _settings.CustomVisibleKeys.Add(key);
                SaveLocked();
            }
            return key;
        }
    }

    /// <summary>移除自定义标签可见键</summary>
    public void RemoveCustomVisibleKey(string key)
    {
        var normalized = NormalizeKey(key);
        if (normalized == null) return;

        lock (_lock)
        {
            RefreshIfChangedLocked();
            if (_settings.CustomVisibleKeys.Remove(normalized))
                SaveLocked();
        }
    }

    /// <summary>规范化自定义标签键（同 Lyrico：去空白、大写、≤64、不含换行）；非法返回 null</summary>
    public static string? NormalizeKey(string input)
    {
        var key = input?.Trim();
        if (string.IsNullOrEmpty(key)) return null;
        if (key.Length > 64) return null;
        if (key.IndexOf('\n') >= 0 || key.IndexOf('\r') >= 0) return null;
        return key.ToUpperInvariant();
    }

    private void SaveLocked()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_settings, JsonOptions));
            _lastWriteUtc = File.GetLastWriteTimeUtc(FilePath);
        }
        catch
        {
            // 持久化失败不阻断编辑会话
        }
    }

    /// <summary>文件被其他实例改动时重载；无改动则零开销</summary>
    private void RefreshIfChangedLocked()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                if (_settings.Version != 1 || _settings.FieldOverrides.Count > 0 || _settings.CustomVisibleKeys.Count > 0)
                {
                    _settings = new EditorSettings();
                    _lastWriteUtc = DateTime.MinValue;
                }
                return;
            }

            var mtime = File.GetLastWriteTimeUtc(FilePath);
            if (mtime > _lastWriteUtc)
            {
                try
                {
                    var loaded = JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(FilePath), JsonOptions);
                    _settings = loaded ?? new EditorSettings();
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