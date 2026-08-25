using Microsoft.Maui.Controls;

namespace CatClawMusic.Plugins.Lrclib.Lyrico;

/// <summary>
/// Lyrico 源测试页：输入歌曲信息 → 调用指定源 getLyrics → 显示命中行数 + 前 20 行预览。
/// 用于导入+配置后验证取词是否正常。纯 C# 代码构建 UI。
/// </summary>
public class LyricoSourceTestPage : ContentPage
{
    private readonly LyricoSourceTestViewModel _vm;

    public LyricoSourceTestPage(LyricoSourceTestViewModel vm)
    {
        _vm = vm;
        BindingContext = _vm;

        Title = _vm.SourceName + " · 测试";
        BackgroundColor = ThemeHelper.Color("WindowBackgroundColor", "#1A1838");

        var titleLabel = new Label
        {
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 8, 0, 4),
        };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");
        titleLabel.SetBinding(Label.TextProperty, nameof(LyricoSourceTestViewModel.SourceName));

        var hint = new Label
        {
            Text = "输入歌曲信息测试该源能否取到歌词",
            FontSize = 12,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 0, 8),
        };
        hint.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");

        var titleEntry = MakeEntry("歌名（必填）", nameof(LyricoSourceTestViewModel.TestTitle));
        var artistEntry = MakeEntry("艺人", nameof(LyricoSourceTestViewModel.TestArtist));
        var albumEntry = MakeEntry("专辑（可空）", nameof(LyricoSourceTestViewModel.TestAlbum));
        var durEntry = MakeEntry("时长（秒，可空）", nameof(LyricoSourceTestViewModel.TestDuration));
        durEntry.Keyboard = Keyboard.Numeric;

        var runButton = new Button
        {
            Text = "测试取词",
            FontSize = 14,
            TextColor = Colors.White,
            BackgroundColor = ThemeHelper.Color("PrimaryColor", "#8C7BFF"),
            CornerRadius = 14,
            Padding = new Thickness(16, 8),
            Margin = new Thickness(0, 8, 0, 4),
        };
        runButton.SetBinding(Button.CommandProperty, nameof(LyricoSourceTestViewModel.RunTestCommand));
        runButton.SetBinding(Button.IsEnabledProperty,
            new Binding(nameof(LyricoSourceTestViewModel.IsBusy)) { Converter = new InverseBooleanConverter(), Source = _vm });

        var status = new Label { FontSize = 13, LineBreakMode = LineBreakMode.WordWrap };
        status.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
        status.SetBinding(Label.TextProperty, nameof(LyricoSourceTestViewModel.StatusText));

        var preview = new Label
        {
            FontSize = 12,
            LineBreakMode = LineBreakMode.WordWrap,
            FontFamily = "monospace",
            Margin = new Thickness(0, 8, 0, 0),
        };
        preview.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");
        preview.SetBinding(Label.TextProperty, nameof(LyricoSourceTestViewModel.PreviewText));
        preview.SetBinding(VisualElement.IsVisibleProperty, nameof(LyricoSourceTestViewModel.HasResult));

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(16, 4),
                Spacing = 8,
                Children = { titleLabel, hint, titleEntry, artistEntry, albumEntry, durEntry, runButton, status, preview },
            }
        };
    }

    private Entry MakeEntry(string placeholder, string binding)
    {
        var e = new Entry { Placeholder = placeholder, FontSize = 14 };
        e.SetDynamicResource(Entry.TextColorProperty, "TextPrimaryColor");
        e.SetDynamicResource(Entry.PlaceholderColorProperty, "TextSecondaryColor");
        e.SetBinding(Entry.TextProperty, new Binding(binding, BindingMode.TwoWay));
        return e;
    }
}
