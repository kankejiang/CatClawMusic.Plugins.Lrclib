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

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
        };
        root.Add(hint, 0, 0);
        root.Add(importButton, 0, 1);
        root.Add(status, 0, 2);
        root.Add(list, 0, 3);
        Content = root;
        WideAdapt.Attach(this);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 从配置/测试/导入返回时刷新，保证列表状态最新。
        _vm.RefreshSources();
    }

    /// <summary>源插件行：图标 + 名称 + 能力 · 目录 + 按钮组。</summary>
    private View BuildSourceRow()
    {
        // 图标：manifest.icon 指向的图片覆盖在音符占位上；无图标时占位透出。
        var placeholder = new Label
        {
            Text = "♪",
            FontSize = 18,
            TextColor = ThemeHelper.Color("PrimaryColor", "#8C7BFF"),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        var iconImage = new Image { WidthRequest = 38, HeightRequest = 38, Aspect = Aspect.AspectFill };
        iconImage.SetBinding(Image.SourceProperty, new Binding(nameof(PluginSourceItem.IconBytes))
        {
            Converter = IconBytesToSourceConverter.Instance,
        });
        var icon = new Border
        {
            WidthRequest = 38,
            HeightRequest = 38,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 9 },
            BackgroundColor = ThemeHelper.Color("PrimaryColor", "#8C7BFF").WithAlpha(0.25f),
            Content = new Grid { Children = { placeholder, iconImage } },
        };

        var name = ThemeHelper.Label(15, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
        name.SetBinding(Label.TextProperty, nameof(PluginSourceItem.Name));

        var cap = ThemeHelper.Label(11, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        cap.SetDynamicResource(Label.TextColorProperty, "PrimaryColor");
        cap.SetBinding(Label.TextProperty, nameof(PluginSourceItem.CapabilityText));

        var dir = ThemeHelper.Label(11, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        dir.SetBinding(Label.TextProperty, nameof(PluginSourceItem.Dir));

        // 加载状态：失败时显示真实错误原因（安卓端引擎/脚本诊断关键路径）
        var status = ThemeHelper.Label(11, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        status.SetBinding(Label.TextProperty, nameof(PluginSourceItem.Status));
        status.SetBinding(Label.IsVisibleProperty, new Binding(nameof(PluginSourceItem.HasLoadIssue)));
        status.SetDynamicResource(Label.TextColorProperty, "WarningColor");

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
            Children = { name, cap, dir, status },
        };

        var buttons = new HorizontalStackLayout { Spacing = 6, Children = { config, toggle, test, deleteBtn } };

        var row = new Grid
        {
            Padding = new Thickness(8, 6),
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        row.Add(icon, 0);
        row.Add(text, 1);
        row.Add(buttons, 2);

        var card = LyricoUi.Card(row);
        WideAdapt.AttachHover(card);
        return card;
    }

    /// <summary>操作按钮：命令绑定必须显式 source 到页面 VM（DataTemplate 行内 BindingContext 是条目对象）。</summary>
    private Button MakeButton(string? text, string command)
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
        b.SetBinding(Button.CommandProperty, new Binding(command, source: _vm));
        b.SetBinding(Button.CommandParameterProperty, new Binding("."));
        return b;
    }
}