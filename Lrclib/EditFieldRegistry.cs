namespace CatClawMusic.Plugins.Lrclib;

/// <summary>编辑字段适用场景（对应 Lyrico <c>EditFieldScene</c>）。</summary>
public enum EditFieldScene
{
    /// <summary>单曲编辑（编辑标签页）</summary>
    SingleEdit,

    /// <summary>批量编辑（批量操作页）</summary>
    BatchEdit,
}

/// <summary>编辑字段适用作用域（对应 Lyrico <c>EditFieldScope</c>）。</summary>
public enum EditFieldScope
{
    SingleEdit,
    BatchEdit,
    Both,
}

/// <summary>编辑字段分组（对应 Lyrico <c>EditFieldGroupDefinition</c>）。</summary>
public sealed record EditFieldGroup(string Code, string Title, bool DefaultVisible, int Order);

/// <summary>编辑字段定义（对应 Lyrico <c>EditFieldDefinition</c>）。</summary>
public sealed record EditField(
    string Code,
    string GroupCode,
    string Title,
    bool DefaultVisible,
    int Order,
    EditFieldScope Scope = EditFieldScope.Both,
    bool Configurable = true);

/// <summary>
/// 编辑字段注册表（复刻 Lyrico <c>EditFieldRegistry</c> + <c>EditFieldVisibilityConfig</c>）：
/// 定义编辑页/批量编辑页可配置的字段分组与字段，支持按 分组/字段 开关控制实际渲染字段，
/// 以及按场景（单曲/批量）判断字段是否可见。
/// 字段列表镜像 Lyrico；当前编辑页仅渲染有宿主字段支持的项，其余可在设置页配置但不渲染。
/// </summary>
internal static class EditFieldRegistry
{
    // ── 分组代码 ──
    public const string GROUP_BASIC_INFO = "basic_info";
    public const string GROUP_TRACK_DETAILS = "track_details";
    public const string GROUP_CREDITS_OTHER = "credits_other";
    public const string GROUP_LYRICS = "lyrics";
    public const string GROUP_COVER = "cover";

    public static readonly IReadOnlyList<EditFieldGroup> Groups = new List<EditFieldGroup>
    {
        new(GROUP_BASIC_INFO, "基本信息", true, 10),
        new(GROUP_TRACK_DETAILS, "音轨信息", true, 20),
        new(GROUP_CREDITS_OTHER, "署名与备注", true, 30),
        new(GROUP_LYRICS, "歌词", true, 40),
        new(GROUP_COVER, "封面", true, 50),
    };

    public static readonly IReadOnlyList<EditField> Fields = new List<EditField>
    {
        // 基本信息
        new($"{GROUP_BASIC_INFO}.title", GROUP_BASIC_INFO, "标题", true, 10),
        new($"{GROUP_BASIC_INFO}.artist", GROUP_BASIC_INFO, "艺人", true, 20),
        new($"{GROUP_BASIC_INFO}.album_artist", GROUP_BASIC_INFO, "专辑艺人", true, 30),
        new($"{GROUP_BASIC_INFO}.album", GROUP_BASIC_INFO, "专辑", true, 40),
        new($"{GROUP_BASIC_INFO}.date", GROUP_BASIC_INFO, "年份", true, 50),
        new($"{GROUP_BASIC_INFO}.genre", GROUP_BASIC_INFO, "流派", true, 70),

        // 音轨信息
        new($"{GROUP_TRACK_DETAILS}.track_number", GROUP_TRACK_DETAILS, "音轨号", true, 10),
        new($"{GROUP_TRACK_DETAILS}.disc_number", GROUP_TRACK_DETAILS, "碟号", true, 20),

        // 署名与备注
        new($"{GROUP_CREDITS_OTHER}.composer", GROUP_CREDITS_OTHER, "作曲", true, 10),
        new($"{GROUP_CREDITS_OTHER}.lyricist", GROUP_CREDITS_OTHER, "作词", true, 20),
        new($"{GROUP_CREDITS_OTHER}.copyright", GROUP_CREDITS_OTHER, "版权", false, 30),
        new($"{GROUP_CREDITS_OTHER}.comment", GROUP_CREDITS_OTHER, "注释", false, 40),

        // 歌词
        new($"{GROUP_LYRICS}.lyrics", GROUP_LYRICS, "歌词", true, 10),
        new($"{GROUP_LYRICS}.lyrics_offset", GROUP_LYRICS, "歌词偏移", true, 20, EditFieldScope.BatchEdit),

        // 封面
        new($"{GROUP_COVER}.picture", GROUP_COVER, "封面", true, 10),
        new($"{GROUP_COVER}.rating", GROUP_COVER, "评分", true, 20),
    };

    private static readonly Dictionary<string, EditFieldGroup> GroupMap = Groups.ToDictionary(g => g.Code);
    private static readonly Dictionary<string, EditField> FieldMap = Fields.ToDictionary(f => f.Code);

    public static EditFieldGroup? GroupOf(string code) => GroupMap.GetValueOrDefault(code);
    public static EditField? FieldOf(string code) => FieldMap.GetValueOrDefault(code);

    public static IReadOnlyList<EditField> FieldsOf(string groupCode)
        => Fields.Where(f => f.GroupCode == groupCode).OrderBy(f => f.Order).ToList();
}

/// <summary>
/// 编辑字段可见性配置解析（复刻 Lyrico <c>EditFieldVisibilityConfig</c>）：
/// 读 <see cref="EditorSettingsStore"/> 的可见性覆盖，提供 分组/字段 开关判断与场景可见性判断。
/// </summary>
internal static class EditFieldConfig
{
    /// <summary>分组开关状态（覆盖优先，无覆盖回退默认）</summary>
    public static bool IsGroupChecked(EditorSettings settings, EditFieldGroup group)
        => settings.FieldOverrides.TryGetValue(group.Code, out var v) ? v : group.DefaultVisible;

    /// <summary>字段开关状态</summary>
    public static bool IsFieldChecked(EditorSettings settings, EditField field)
        => settings.FieldOverrides.TryGetValue(field.Code, out var v) ? v : field.DefaultVisible;

    /// <summary>字段在指定场景下是否应渲染：场景支持 &amp;&amp; 分组开关 &amp;&amp; 字段开关</summary>
    public static bool IsVisibleInScene(EditorSettings settings, EditField field, EditFieldScene scene)
    {
        if (!SupportsScope(field.Scope, scene)) return false;
        var group = EditFieldRegistry.GroupOf(field.GroupCode);
        if (group == null) return false;
        return IsGroupChecked(settings, group) && IsFieldChecked(settings, field);
    }

    private static bool SupportsScope(EditFieldScope scope, EditFieldScene scene) => scope switch
    {
        EditFieldScope.SingleEdit => scene == EditFieldScene.SingleEdit,
        EditFieldScope.BatchEdit => scene == EditFieldScene.BatchEdit,
        _ => true,
    };
}