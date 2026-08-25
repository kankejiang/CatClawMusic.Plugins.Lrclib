using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Plugins.Lrclib.Lyrico;

/// <summary>
/// Lyrico 源插件配置页 ViewModel：加载 manifest 的 ConfigFields + 用户已存值，
/// 按字段类型渲染表单，保存后刷新 hub 使运行中的脚本宿主重载配置。
/// </summary>
public partial class LyricoSourceConfigViewModel : ObservableObject
{
    private readonly LyricoLyricsHub _hub;
    private readonly string _pluginDir;
    private LyricoSourceConfigStore _store;
    private List<LyricoConfigField> _fields = new();

    [ObservableProperty] private string pluginName = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusText = "";

    public ObservableCollection<LyricoConfigFieldItem> Fields { get; } = new();

    public LyricoSourceConfigViewModel(LyricoLyricsHub hub, string pluginDir)
    {
        _hub = hub;
        _pluginDir = pluginDir;
        _store = hub.GetConfigStore(pluginDir);
        Load();
    }

    /// <summary>加载 manifest 配置字段 + 当前用户值，按 group 排序后填充表单。</summary>
    public void Load()
    {
        Fields.Clear();
        var manifest = _hub.GetManifest(_pluginDir);
        PluginName = manifest?.Name ?? _pluginDir;
        _fields = manifest?.ConfigFields ?? new List<LyricoConfigField>();

        if (_fields.Count == 0)
        {
            StatusText = "该插件未声明配置项";
            return;
        }

        var values = _store.Values;
        foreach (var f in _fields.OrderBy(x => x.Group, StringComparer.Ordinal).ThenBy(x => x.Title))
        {
            var val = values.TryGetValue(f.Key, out var v) ? v : f.DefaultValue;
            var item = new LyricoConfigFieldItem(f) { Value = val };
            item.PropertyChanged += (_, _) => ReevaluateVisibility();
            Fields.Add(item);
        }
        ReevaluateVisibility();
        StatusText = $"共 {_fields.Count} 项配置";
    }

    /// <summary>重新评估各字段依赖可见性（任一值变更后调用）。</summary>
    public void ReevaluateVisibility()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in Fields) values[f.Key] = f.Value ?? "";
        foreach (var f in Fields)
            f.IsVisible = LyricoConfigDependency.IsSatisfied(f.Dependency, values);
    }

    /// <summary>保存配置：写回存储 + 刷新 hub（脚本宿主下次请求重载配置）。</summary>
    [RelayCommand]
    private void Save()
    {
        IsBusy = true;
        try
        {
            foreach (var f in Fields)
                _store.Set(f.Key, f.Value ?? "");
            _store.Save();
            _hub.Refresh();
            StatusText = "配置已保存";
        }
        catch (Exception ex)
        {
            StatusText = "保存失败：" + ex.Message;
        }
        finally { IsBusy = false; }
    }
}

/// <summary>配置字段表单项（含元数据 + 可观察的当前值 + 可见性）。</summary>
public partial class LyricoConfigFieldItem : ObservableObject
{
    private readonly LyricoConfigField _field;

    public LyricoConfigFieldItem(LyricoConfigField field) => _field = field;

    public string Key => _field.Key;
    public string Title => _field.Title;
    public string? Summary => _field.Summary;
    public string Group => _field.Group;
    public string Type => _field.Type;
    public bool Required => _field.Required;
    public IReadOnlyList<LyricoConfigOption> Options => _field.Options;
    public System.Text.Json.JsonElement? Dependency => _field.Dependency;

    [ObservableProperty] private string value = "";
    [ObservableProperty] private bool isVisible = true;

    /// <summary>Value 变更时同步通知计算属性（SwitchValue/SelectedIndex）的绑定刷新。</summary>
    partial void OnValueChanged(string value)
    {
        OnPropertyChanged(nameof(SwitchValue));
        OnPropertyChanged(nameof(SelectedIndex));
    }

    /// <summary>switch 类型的布尔便捷访问（Value 存 "true"/"false" 字符串）。</summary>
    public bool SwitchValue
    {
        get => string.Equals(Value, "true", StringComparison.OrdinalIgnoreCase);
        set => Value = value ? "true" : "false";
    }

    /// <summary>下拉的选中索引（按 Options 匹配 Value）。</summary>
    public int SelectedIndex
    {
        get
        {
            for (int i = 0; i < Options.Count; i++)
                if (string.Equals(Options[i].Value, Value, StringComparison.Ordinal)) return i;
            return -1;
        }
        set
        {
            if (value >= 0 && value < Options.Count)
            {
                var opt = Options[value];
                if (!string.Equals(opt.Value, Value, StringComparison.Ordinal))
                    Value = opt.Value;
            }
        }
    }

    /// <summary>是否 markdown 类型（只展示文本，无输入）。</summary>
    public bool IsMarkdown => string.Equals(Type, "markdown", StringComparison.OrdinalIgnoreCase);
}
