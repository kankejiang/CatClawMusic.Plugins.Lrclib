using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 编辑字段可见性设置页（复刻 Lyrico <c>EditFieldVisibilitySettingsScreen</c>）：
/// 按分组展示可配置字段，分组开关 + 字段开关，关掉分组则组内字段禁用（但保留各自开关状态），
/// 底部「恢复默认」一键清空覆盖。所有改动立即持久化并供编辑页生效。
/// </summary>
public class EditFieldVisibilityPage : ContentPage
{
    private readonly EditorSettingsStore _store = new();

    public EditFieldVisibilityPage()
    {
        Title = "编辑字段可见性";
        BackgroundColor = ThemeHelper.Color("WindowBackgroundColor", "#1A1838");
        Content = new ScrollView { Content = BuildContent() };
        WideAdapt.Attach(this, WideAdapt.FormMaxWidth);
    }

    private View BuildContent()
    {
        var stack = new VerticalStackLayout { Spacing = 10, Padding = new Thickness(12, 8) };
        stack.Add(BuildHint());

        var settings = _store.Get();
        foreach (var group in EditFieldRegistry.Groups)
            stack.Add(BuildGroupCard(settings, group));

        stack.Add(BuildResetButton());
        return stack;
    }

    private View BuildHint()
        => ThemeHelper.Card(new Label
        {
            Text = "控制在「编辑标签」页显示哪些字段。关闭分组后组内字段不可用；字段开关只在分组开启时生效。",
            FontSize = 12,
            LineBreakMode = LineBreakMode.WordWrap,
            TextColor = ThemeHelper.Color("TextSecondaryColor", "#C2C6E4"),
        });

    private View BuildGroupCard(EditorSettings settings, EditFieldGroup group)
    {
        var groupChecked = EditFieldConfig.IsGroupChecked(settings, group);

        var groupSwitch = new Switch { IsToggled = groupChecked, VerticalOptions = LayoutOptions.Center };
        groupSwitch.Toggled += (_, e) => _store.SetFieldOverride(group.Code, e.Value);

        var title = ThemeHelper.Label(15, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
        title.Text = group.Title;

        var header = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        header.Add(title, 0);
        header.Add(groupSwitch, 1);

        var inner = new VerticalStackLayout { Spacing = 8, Children = { header } };

        var fields = EditFieldRegistry.FieldsOf(group.Code)
            .Where(f => f.Configurable)
            .OrderBy(f => f.Order);
        foreach (var field in fields)
        {
            var fieldChecked = EditFieldConfig.IsFieldChecked(settings, field);
            var row = BuildFieldRow(field.Title, fieldChecked, groupChecked, field.Code);
            inner.Add(row);
        }

        return ThemeHelper.Card(inner, corner: 16);
    }

    private View BuildFieldRow(string label, bool checkedState, bool groupEnabled, string fieldCode)
    {
        var text = ThemeHelper.Label(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        text.Text = label;

        var sw = new Switch
        {
            IsToggled = checkedState,
            IsEnabled = groupEnabled,
            VerticalOptions = LayoutOptions.Center,
        };
        sw.Toggled += (_, e) => _store.SetFieldOverride(fieldCode, e.Value);

        var row = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        row.Add(text, 0);
        row.Add(sw, 1);
        return row;
    }

    private View BuildResetButton()
    {
        var reset = new Button
        {
            Text = "恢复默认",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeHelper.Color("TextSecondaryColor", "#C2C6E4"),
            BackgroundColor = Colors.Transparent,
            CornerRadius = 16,
            HeightRequest = 44,
        };
        reset.Clicked += async (_, _) =>
        {
            var ok = await DisplayAlert("恢复默认", "将清空所有可见性自定义，恢复默认显示全部字段。", "恢复默认", "取消");
            if (ok)
            {
                _store.ResetFieldOverrides();
                Content = new ScrollView { Content = BuildContent() };
            }
        };
        return reset;
    }
}