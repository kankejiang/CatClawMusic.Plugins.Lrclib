using System.Text.Json.Serialization;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>自定义拆分分隔符（对应 Lyrico <c>CustomArtistSeparator</c>）</summary>
public sealed class CustomArtistSeparator
{
    public string Value { get; set; } = "";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool Enabled { get; set; } = true;
}

/// <summary>自定义不拆分艺人（对应 Lyrico <c>CustomNoSplitArtist</c>）</summary>
public sealed class CustomNoSplitArtist
{
    public string Name { get; set; } = "";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool Enabled { get; set; } = true;
}

/// <summary>内置拆分分隔符（对应 Lyrico <c>BuiltinArtistSeparator</c>）</summary>
public sealed class BuiltinArtistSeparator
{
    public string Id { get; }
    public string Value { get; }
    public bool DefaultEnabled { get; }
    public string DisplayName { get; }
    public BuiltinArtistSeparator(string id, string value, bool defaultEnabled, string? displayName = null)
    {
        Id = id; Value = value; DefaultEnabled = defaultEnabled; DisplayName = displayName ?? value;
    }
}

/// <summary>内置不拆分艺人（对应 Lyrico <c>BuiltinNoSplitArtist</c>）</summary>
public sealed class BuiltinNoSplitArtist
{
    public string Id { get; }
    public string Name { get; }
    public bool DefaultEnabled { get; } = true;
    public BuiltinNoSplitArtist(string id, string name) { Id = id; Name = name; }
}

/// <summary>
/// 艺术家拆分配置（复刻 Lyrico <c>ArtistSplitConfig</c>）：
/// 控点艺人库按哪些分隔符把多艺人拆开归组，以及哪些整名艺人保持不拆分。
/// </summary>
public sealed class ArtistSplitConfig
{
    /// <summary>是否启用拆分</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>内置分隔符覆盖：分隔符 ID → 是否启用（未记录的用 <see cref="BuiltinArtistSeparator.DefaultEnabled"/>）</summary>
    public Dictionary<string, bool> BuiltinSeparatorOverrides { get; set; } = new();

    /// <summary>被隐藏的内置分隔符 ID（不参与拆分也不显示，极端清理用）</summary>
    public HashSet<string> HiddenBuiltinSeparatorIds { get; set; } = new();

    /// <summary>自定义分隔符列表</summary>
    public List<CustomArtistSeparator> CustomSeparators { get; set; } = new();

    /// <summary>内置不拆分艺人覆盖：ID → 是否启用</summary>
    public Dictionary<string, bool> BuiltinNoSplitArtistOverrides { get; set; } = new();

    /// <summary>自定义不拆分艺人列表</summary>
    public List<CustomNoSplitArtist> CustomNoSplitArtists { get; set; } = new();
}

/// <summary>
/// 拆分默认值与生效规则（复刻 Lyrico <c>ArtistSplitDefaults</c>）。
/// </summary>
public static class ArtistSplitDefaults
{
    public static readonly IReadOnlyList<BuiltinArtistSeparator> BuiltinSeparators = new List<BuiltinArtistSeparator>
    {
        new("slash", "/", true),
        new("fullwidth_slash", "／", true),
        new("semicolon", ";", true),
        new("fullwidth_semicolon", "；", true),
        new("comma", ",", true),
        new("fullwidth_comma", "，", true),
        new("ideographic_comma", "、", true),
        new("ampersand", "&", false),
        new("feat_dot", " feat. ", false, "feat."),
        new("ft_dot", " ft. ", false, "ft."),
        new("featuring", " featuring ", false, "featuring"),
    };

    public static readonly IReadOnlyList<BuiltinNoSplitArtist> BuiltinNoSplitArtists = new List<BuiltinNoSplitArtist>
    {
        new("simon_and_garfunkel", "Simon & Garfunkel"),
        new("earth_wind_and_fire", "Earth, Wind & Fire"),
        new("bump_of_chicken", "BUMP OF CHICKEN"),
    };

    /// <summary>规范化艺人键：去首尾空白、压缩连续空格、小写（艺人去重/不拆分判定用）</summary>
    public static string NormalizedKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var ch in name!.Trim())
        {
            if (ch == ' ' || ch == '\t')
            {
                if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
            }
            else
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }
        return sb.ToString();
    }

    /// <summary>生效的拆分分隔符列表（内置按配置过滤 + 自定义启用项，去空去重，去逗/分号重叠按 trim 去重）</summary>
    public static List<string> EffectiveSeparators(ArtistSplitConfig config)
    {
        var builtin = BuiltinSeparators
            .Where(s => !config.HiddenBuiltinSeparatorIds.Contains(s.Id))
            .Where(s => config.BuiltinSeparatorOverrides.TryGetValue(s.Id, out var v) ? v : s.DefaultEnabled)
            .Select(s => s.Value);

        var custom = config.CustomSeparators
            .Where(s => s.Enabled)
            .Select(s => s.Value);

        return builtin.Concat(custom)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .DistinctBy(v => v.Trim())
            .ToList();
    }

    /// <summary>生效的不拆分艺人名列表（内置按配置过滤 + 自定义启用项，按规范化键去重）</summary>
    public static List<string> EffectiveNoSplitArtistNames(ArtistSplitConfig config)
    {
        var builtin = BuiltinNoSplitArtists
            .Where(a => config.BuiltinNoSplitArtistOverrides.TryGetValue(a.Id, out var v) ? v : a.DefaultEnabled)
            .Select(a => a.Name);

        var custom = config.CustomNoSplitArtists
            .Where(a => a.Enabled)
            .Select(a => a.Name);

        return builtin.Concat(custom)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .DistinctBy(NormalizedKey)
            .ToList();
    }

    /// <summary>生效的不拆分艺人规范化键集合</summary>
    public static HashSet<string> EffectiveNoSplitArtists(ArtistSplitConfig config)
        => EffectiveNoSplitArtistNames(config).Select(NormalizedKey).ToHashSet();
}

/// <summary>
/// 艺人名称拆分器（复刻 Lyrico <c>ArtistNameSplitter</c>）：
/// 按生效分隔符把多艺人串拆开，遇到「不拆分艺人」时整段保留；结果按规范化键去重。
/// </summary>
public static class ArtistNameSplitter
{
    public static List<string> SplitArtists(string? rawArtist, ArtistSplitConfig config)
    {
        var raw = rawArtist?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
        if (!config.Enabled) return new List<string> { raw };

        if (ArtistSplitDefaults.EffectiveNoSplitArtists(config).Contains(ArtistSplitDefaults.NormalizedKey(raw)))
            return new List<string> { raw };

        var separators = ArtistSplitDefaults.EffectiveSeparators(config);
        if (separators.Count == 0) return new List<string> { raw };

        var separatorRegex = ToAnchoredRegex(separators);
        var noSplitRegex = ToAnchoredRegex(
            ArtistSplitDefaults.EffectiveNoSplitArtistNames(config)
                .Where(n => separators.Any(s => n.Contains(s, StringComparison.OrdinalIgnoreCase)))
                .ToList());

        return SplitPreserving(raw, separatorRegex, noSplitRegex)
            .DistinctBy(ArtistSplitDefaults.NormalizedKey)
            .ToList();
    }

    private static List<string> SplitPreserving(string raw, string? separatorRegex, string? noSplitRegex)
    {
        var artists = new List<string>();
        var current = new System.Text.StringBuilder();
        var index = 0;

        while (index < raw.Length)
        {
            var noSplit = AnchoredMatchAt(raw, index, noSplitRegex);
            if (noSplit != null)
            {
                current.Append(noSplit);
                index += noSplit.Length;
                continue;
            }

            var sep = AnchoredMatchAt(raw, index, separatorRegex);
            if (sep != null)
            {
                Flush(current, artists);
                index += sep.Length;
                continue;
            }

            current.Append(raw[index]);
            index++;
        }

        Flush(current, artists);
        return artists;
    }

    /// <summary>把分隔符/不拆分列表转为锚定（^ 处）匹配正则；空列表返回 null</summary>
    private static string? ToAnchoredRegex(IEnumerable<string> values)
    {
        var list = values.ToList();
        if (list.Count == 0) return null;
        var pattern = string.Join("|", list
            .OrderByDescending(v => v.Length)
            .Select(Regex_Escape));
        return "^(" + pattern + ")";
    }

    /// <summary>在 input[index..] 处锚定匹配，返回匹配文本；不匹配返回 null</summary>
    private static string? AnchoredMatchAt(string input, int index, string? regex)
    {
        if (regex == null || index >= input.Length) return null;
        var match = System.Text.RegularExpressions.Regex.Match(input, regex,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        if (match.Success && match.Index == index) return match.Value;
        return null;
    }

    private static string Regex_Escape(string s)
        => System.Text.RegularExpressions.Regex.Escape(s);

    private static void Flush(System.Text.StringBuilder current, List<string> artists)
    {
        var value = current.ToString().Trim();
        if (value.Length > 0) artists.Add(value);
        current.Clear();
    }
}