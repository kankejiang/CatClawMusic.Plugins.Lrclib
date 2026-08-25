using System.ComponentModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 插件管理器总页：统一管理 Lyrico 源插件（对齐 Lyrico PluginManagerScreen）。
/// 顶部导入按钮 + 已装源列表，每个仓显示 能力/加载状态，支持 启停/配置/测试/卸载。
/// </summary>
public class PluginManagerPage : ContentPage
{
    private readonly PluginManagerViewModel _vm;

    public PluginManagerPage()
    {
        _vm = new PluginManagerViewModel(PluginHost.LyricoHub);
        BindingContext = _vm;
        Title = "源插件管理";
        BackgroundColor = ThemeHelper.Color("WindowBackgroundColor", "#1A1838");

        var hint = new Label
        {
            FontSize = 12,
            LineBreakMode = LineBreakMode.WordWrap,
            Margin = new Thickness(16, 6, 16, 0),
        };
        hint.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
        hint.Text = "Lyrico 源插件：实现宿主 API 的 JS 脚本（netease/qq/kugou/soda/apple 等），LRCLIB 未命中时兜底取词。导入 .zip 安装，配置后可用「测试」验证取词。";

        var importButton = new Button
        {
            Text = "导入源插件(.zip)",
            FontSize = 14,
            TextColor = Colors.White,
            BackgroundColor = ThemeHelper.Color("PrimaryColor", "#8C7BFF"),
            CornerRadius = 14,
            Padding = new Thickness(16, 8),
            Margin = new Thickness(16, 10, 16, 0),
        };
        importButton.SetBinding(Button.CommandProperty, nameof(PluginManagerViewModel.ImportCommand));
        importButton.SetBinding(Button.IsEnabledProperty,
            new Binding(nameof(PluginManagerViewModel.IsBusy)) { Converter = new InverseBooleanConverter(), Source = _vm });

        var status = new Label { FontSize = 12, LineBreakMode = LineBreakMode.WordWrap, Margin = new Thickness(16, 8, 16, 0) };
        status.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
        status.SetBinding(Label.TextProperty, nameof(PluginManagerViewModel.StatusText));

        var list = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            Margin = new Thickness(8, 8, 8, 0),
            ItemTemplate = new DataTemplate(BuildSourceRow),
        };
        list.SetBinding(CollectionView.ItemsSourceProperty, nameof(PluginManagerViewModel.Sources));

        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
            Children = { hint, importButton, status, list },
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 从配置/测试/导入返回时刷新，保证列表状态最新。
        _vm.RefreshSources();
    }

    /// <summary>源插件行：名称 + 能力 · 目录 + 按钮组。</summary>
    private View BuildSourceRow()
    {
        var name = ThemeHelper.Label(15, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
        name.SetBinding(Label.TextProperty, nameof(PluginSourceItem.Name));

        var cap = ThemeHelper.Label(11, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        cap.SetDynamicResource(Label.TextColorProperty, "PrimaryColor");
        cap.SetBinding(Label.TextProperty, nameof(PluginSourceItem.CapabilityText));

        var dir = ThemeHelper.Label(11, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        dir.SetBinding(Label.TextProperty, nameof(PluginSourceItem.Dir));

        var config = MakeButton("配置", nameof(PluginManagerViewModel.OpenConfigCommand));
        config.SetBinding(Button.IsVisibleProperty, nameof(PluginSourceItem.HasConfig));
        var toggle = MakeButton(null, nameof(PluginManagerViewModel.ToggleSourceCommand));
        toggle.SetBinding(Button.TextProperty, nameof(PluginSourceItem.ToggleText));
        var test = MakeButton("测试", nameof(PluginManagerViewModel.OpenTestCommand));
        var deleteBtn = MakeButton("卸载", nameof(PluginManagerViewModel.DeleteSourceCommand));

        var text = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Spacing = 1,
            Children = { name, cap, dir },
        };

        var buttons = new HorizontalStackLayout { Spacing = 6, Children = { config, toggle, test, deleteBtn } };

        var row = new Grid
        {
            Padding = new Thickness(8, 6),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        row.Add(text, 0);
        row.Add(buttons, 1);

        return LyricoUi.Card(row);
    }

    private static Button MakeButton(string? text, string command)
    {
        var b = new Button
        {
            Text = text ?? "",
            FontSize = 12,
            Padding = new Thickness(10, 3),
            BackgroundColor = ThemeHelper.Color("PrimaryColor", "#8C7BFF"),
            TextColor = Colors.White,
            CornerRadius = 14,
        };
        b.SetBinding(Button.CommandProperty, command);
        b.SetBinding(Button.CommandParameterProperty, new Binding("."));
        return b;
    }
}