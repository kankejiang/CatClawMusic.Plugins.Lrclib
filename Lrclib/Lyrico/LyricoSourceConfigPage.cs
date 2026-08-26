using Microsoft.Maui.Controls;

namespace CatClawMusic.Plugins.Lrclib.Lyrico;

/// <summary>
/// Lyrico 源插件配置页：按 manifest 的 configFields 渲染表单（text/password/number/
/// switch/dropdown/textarea/markdown），依赖（match/and/or/not）控制字段可见性。
/// 保存后刷新 hub，运行中的脚本宿主下次请求重载配置。纯 C# 代码构建 UI。
/// </summary>
public class LyricoSourceConfigPage : ContentPage
{
    private readonly LyricoSourceConfigViewModel _vm;

    public LyricoSourceConfigPage(LyricoSourceConfigViewModel vm)
    {
        _vm = vm;
        BindingContext = _vm;

        Title = _vm.PluginName + " · 配置";
        BackgroundColor = ThemeHelper.Color("WindowBackgroundColor", "#1A1838");

        var status = new Label { FontSize = 12, LineBreakMode = LineBreakMode.WordWrap };
        status.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
        status.SetBinding(Label.TextProperty, nameof(LyricoSourceConfigViewModel.StatusText));

        var saveButton = new Button
        {
            Text = "保存配置",
            FontSize = 14,
            TextColor = Colors.White,
            BackgroundColor = ThemeHelper.Color("PrimaryColor", "#8C7BFF"),
            CornerRadius = 14,
            Padding = new Thickness(16, 8),
        };
        saveButton.SetBinding(Button.CommandProperty, nameof(LyricoSourceConfigViewModel.SaveCommand));
        saveButton.SetBinding(Button.IsEnabledProperty,
            new Binding(nameof(LyricoSourceConfigViewModel.IsBusy)) { Converter = new InverseBooleanConverter(), Source = _vm });

        var form = new VerticalStackLayout { Spacing = 16, Padding = new Thickness(16, 8) };
        BuildFields(form);

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(0, 12),
                Spacing = 12,
                Children = { status, form, saveButton },
            }
        };
    }

    /// <summary>按字段类型构建表单控件。每个字段包在一个可见性容器里（绑定 IsVisible）。</summary>
    private void BuildFields(VerticalStackLayout form)
    {
        if (_vm.Fields.Count == 0) return;

        string? lastGroup = null;
        foreach (var field in _vm.Fields)
        {
            // 分组标题（组名变化时插入分隔标题）
            if (!string.IsNullOrEmpty(field.Group) && field.Group != lastGroup)
            {
                lastGroup = field.Group;
                var groupLabel = new Label
                {
                    Text = field.Group,
                    FontSize = 13,
                    FontAttributes = FontAttributes.Bold,
                    Margin = new Thickness(0, 8, 0, 0),
                };
                groupLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
                form.Children.Add(groupLabel);
            }

            var container = new VerticalStackLayout { Spacing = 4 };
            // 关键：容器 BindingContext 指向该字段项，否则所有子控件绑定解析到页面 VM 而静默失效
            container.BindingContext = field;
            container.SetBinding(VisualElement.IsVisibleProperty, nameof(LyricoConfigFieldItem.IsVisible));

            // 标题 + 必填标记
            var titleRow = new HorizontalStackLayout { Spacing = 4 };
            var titleLabel = new Label
            {
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center,
            };
            titleLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");
            titleLabel.SetBinding(Label.TextProperty, nameof(LyricoConfigFieldItem.Title));
            titleRow.Children.Add(titleLabel);
            if (field.Required)
            {
                titleRow.Children.Add(new Label
                {
                    Text = "*",
                    FontSize = 14,
                    TextColor = Colors.OrangeRed,
                    VerticalOptions = LayoutOptions.Center,
                });
            }
            container.Children.Add(titleRow);

            // 摘要（可选）
            if (!string.IsNullOrWhiteSpace(field.Summary))
            {
                var summary = new Label { FontSize = 11, LineBreakMode = LineBreakMode.WordWrap };
                summary.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
                summary.SetBinding(Label.TextProperty, nameof(LyricoConfigFieldItem.Summary));
                container.Children.Add(summary);
            }

            // 类型对应控件
            container.Children.Add(BuildControl(field));
            form.Children.Add(container);
        }
    }

    /// <summary>按字段类型构建输入控件。</summary>
    private View BuildControl(LyricoConfigFieldItem field)
    {
        var t = field.Type?.ToLowerInvariant() ?? "text";
        switch (t)
        {
            case "switch":
                var sw = new Switch
                {
                    HorizontalOptions = LayoutOptions.Start,
                    OnColor = ThemeHelper.Color("PrimaryColor", "#8C7BFF"),
                };
                sw.SetBinding(Switch.IsToggledProperty,
                    new Binding(nameof(LyricoConfigFieldItem.SwitchValue), BindingMode.TwoWay));
                return sw;

            case "dropdown":
                var picker = new Picker
                {
                    FontSize = 14,
                    Title = "选择…",
                    ItemDisplayBinding = new Binding(nameof(LyricoConfigOption.Label)),
                };
                picker.SetBinding(Picker.ItemsSourceProperty, nameof(LyricoConfigFieldItem.Options));
                picker.SetBinding(Picker.SelectedIndexProperty,
                    new Binding(nameof(LyricoConfigFieldItem.SelectedIndex), BindingMode.TwoWay));
                return picker;

            case "textarea":
                var editor = new Editor
                {
                    FontSize = 14,
                    AutoSize = EditorAutoSizeOption.TextChanges,
                    MinimumHeightRequest = 80,
                    Margin = new Thickness(0, 2),
                };
                editor.SetDynamicResource(Editor.TextColorProperty, "TextPrimaryColor");
                editor.SetDynamicResource(Editor.BackgroundColorProperty, "CardBackgroundColor");
                editor.SetBinding(Editor.TextProperty,
                    new Binding(nameof(LyricoConfigFieldItem.Value), BindingMode.TwoWay));
                return editor;

            case "markdown":
                // 只展示文本，无输入
                var md = new Label { FontSize = 13, LineBreakMode = LineBreakMode.WordWrap };
                md.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
                md.SetBinding(Label.TextProperty, nameof(LyricoConfigFieldItem.Value));
                return md;

            case "password":
                var pwd = new Entry { IsPassword = true, FontSize = 14 };
                pwd.SetDynamicResource(Entry.TextColorProperty, "TextPrimaryColor");
                pwd.SetDynamicResource(Entry.PlaceholderColorProperty, "TextSecondaryColor");
                pwd.SetBinding(Entry.TextProperty,
                    new Binding(nameof(LyricoConfigFieldItem.Value), BindingMode.TwoWay));
                return pwd;

            case "number":
                var num = new Entry { Keyboard = Keyboard.Numeric, FontSize = 14 };
                num.SetDynamicResource(Entry.TextColorProperty, "TextPrimaryColor");
                num.SetBinding(Entry.TextProperty,
                    new Binding(nameof(LyricoConfigFieldItem.Value), BindingMode.TwoWay));
                return num;

            default: // text
                var entry = new Entry { FontSize = 14 };
                entry.SetDynamicResource(Entry.TextColorProperty, "TextPrimaryColor");
                entry.SetDynamicResource(Entry.PlaceholderColorProperty, "TextSecondaryColor");
                entry.SetBinding(Entry.TextProperty,
                    new Binding(nameof(LyricoConfigFieldItem.Value), BindingMode.TwoWay));
                return entry;
        }
    }
}
