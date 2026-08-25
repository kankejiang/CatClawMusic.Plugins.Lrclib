using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.Storage;

namespace CatClawMusic.Plugins.Lrclib.Lyrico;

// ──────────────────────────────────────────────────────────────────────────
//  插件配置字段模型（对齐 Lyrico PluginConfigField / PluginConfigFieldType /
//  PluginConfigOption / PluginConfigDependency）
// ──────────────────────────────────────────────────────────────────────────

/// <summary>Lyrico 源插件声明的配置字段（manifest.json 的 configFields[] 项）。</summary>
public sealed class LyricoConfigField
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Summary { get; set; }
    public string Group { get; set; } = "";
    public string Type { get; set; } = "text";
    public bool Required { get; set; }
    public string DefaultValue { get; set; } = "";
    public List<LyricoConfigOption> Options { get; set; } = new();
    /// <summary>原始依赖 JSON（match/and/or/not），延迟到求值时解析。</summary>
    public JsonElement? Dependency { get; set; }
}

/// <summary>下拉选项。</summary>
public sealed class LyricoConfigOption
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Summary { get; set; }
}

/// <summary>
/// 配置依赖求值器（match/and/or/not 四种，键即类型标记）。
/// 递归求值，参考 Lyrico SourceConfigDependencyEvaluator。
/// </summary>
public static class LyricoConfigDependency
{
    /// <summary>判断依赖是否被当前配置值满足。</summary>
    public static bool IsSatisfied(JsonElement? dep, IReadOnlyDictionary<string, string> values)
    {
        if (dep == null || dep.Value.ValueKind != JsonValueKind.Object) return true;
        var o = dep.Value;
        // match: { key, value }
        if (o.TryGetProperty("match", out var m) && m.ValueKind == JsonValueKind.Object)
        {
            var key = Str(m, "key");
            var val = Str(m, "value");
            return values.TryGetValue(key, out var v) && string.Equals(v, val, StringComparison.Ordinal);
        }
        // and: { conditions: [ ... ] }
        if (o.TryGetProperty("and", out var a) && a.ValueKind == JsonValueKind.Object
            && a.TryGetProperty("conditions", out var conds) && conds.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in conds.EnumerateArray())
                if (!IsSatisfied(c, values)) return false;
            return true;
        }
        // or: { conditions: [ ... ] }
        if (o.TryGetProperty("or", out var or) && or.ValueKind == JsonValueKind.Object
            && or.TryGetProperty("conditions", out var oconds) && oconds.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in oconds.EnumerateArray())
                if (IsSatisfied(c, values)) return true;
            return false;
        }
        // not: { condition: { ... } }
        if (o.TryGetProperty("not", out var n) && n.ValueKind == JsonValueKind.Object
            && n.TryGetProperty("condition", out var nc))
        {
            return !IsSatisfied(nc, values);
        }
        return true;
    }

    private static string Str(JsonElement o, string key)
        => o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}

// ──────────────────────────────────────────────────────────────────────────
//  配置持久化：每个源插件一份 JSON（key→value），存于 LyricoSources/.config/{id}.json
// ──────────────────────────────────────────────────────────────────────────

/// <summary>
/// Lyrico 源插件用户配置存储：按 pluginId 隔离，键值对持久化。
/// 存储路径：AppDataDirectory/Plugin/LyricoSources/.config/{pluginId}.json
/// </summary>
public sealed class LyricoSourceConfigStore
{
    private static readonly string ConfigDir = Path.Combine(
        LyricoSourceCatalog.SourcesRoot, ".config");

    private readonly string _pluginId;
    private readonly string _file;
    private Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public LyricoSourceConfigStore(string pluginId)
    {
        _pluginId = pluginId;
        _file = Path.Combine(ConfigDir, pluginId + ".json");
        Load();
    }

    /// <summary>当前配置值（只读视图）。</summary>
    public IReadOnlyDictionary<string, string> Values => _values;

    /// <summary>取某键值（不存在返回 null）。</summary>
    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;

    /// <summary>设置键值（内存），需调用 <see cref="Save"/> 持久化。</summary>
    public void Set(string key, string value)
    {
        if (string.IsNullOrEmpty(key)) return;
        _values[key] = value ?? "";
    }

    /// <summary>删除键。</summary>
    public void Remove(string key) => _values.Remove(key);

    /// <summary>从磁盘加载（文件缺失/损坏返回空字典）。</summary>
    public void Load()
    {
        try
        {
            if (File.Exists(_file))
            {
                var json = JsonNode.Parse(File.ReadAllText(_file));
                _values = new Dictionary<string, string>(StringComparer.Ordinal);
                if (json is JsonObject obj)
                    foreach (var (k, v) in obj)
                        if (v != null) _values[k] = v.GetValue<string>();
            }
        }
        catch { _values = new Dictionary<string, string>(StringComparer.Ordinal); }
    }

    /// <summary>持久化到磁盘（静默失败）。</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var obj = new JsonObject();
            foreach (var (k, v) in _values) obj[k] = v;
            File.WriteAllText(_file, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>合并 manifest 声明的默认值（仅对用户未设置过的键填充）。</summary>
    public void ApplyDefaults(IEnumerable<LyricoConfigField> fields)
    {
        foreach (var f in fields)
        {
            if (!_values.ContainsKey(f.Key) && !string.IsNullOrEmpty(f.DefaultValue))
                _values[f.Key] = f.DefaultValue;
        }
    }
}
