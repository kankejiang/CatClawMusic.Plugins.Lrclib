using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 统一搜索单条结果预览页：展示所选结果的封面大图 + 歌词预览，
/// 底部「写入标签」一键写入歌词 + 封面。用系统页面导航承载（戳宿主插件页面导航），
/// 不依赖自绘定位，任何平台布局都可靠。
/// </summary>
public sealed class UnifiedSearchPreviewPage : ContentPage
{
    private readonly UnifiedSearchViewModel _vm;

    public UnifiedSearchPreviewPage(UnifiedSearchViewModel vm)
    {
        _vm = vm;
        BindingContext = _vm;

        Title = "结果预览";
        BackgroundColor = GetResourceColor("WindowBackgroundColor", "#1A1838");

        _vm.Applied += async (_, _) =>
        {
            // 写入成功后自动返回搜索结果页
            try { await PluginNav.PopAsync(); } catch { }
        };

        Content = BuildPreviewContent();
    }

    private View BuildPreviewContent()
    {
        // 标题
        var titleLabel = NewLabel(18, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", tail: false);
        titleLabel.TextColor = Text("TextPrimaryColor", "#F7F8FF");
        titleLabel.SetBinding(Label.TextProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.DisplayTitle)}"));

        // 副标题（来源 / 能力）
        var subLabel = NewLabel(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: false);
        subLabel.SetBinding(Label.TextProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.Subtitle)}"));
        subLabel.LineBreakMode = LineBreakMode.WordWrap;

        var badge = NewLabel(11, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", false);
        badge.SetBinding(Label.TextProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.Badge)}"));
        badge.SetDynamicResource(Label.TextColorProperty, "PrimaryColor");

        // 封面大图
        var coverImg = new Image
        {
            HeightRequest = 200,
            WidthRequest = 200,
            Aspect = Aspect.AspectFill,
            HorizontalOptions = LayoutOptions.Center,
        };
        coverImg.SetBinding(Image.SourceProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.HighResCoverUrl)}")
            { Converter = new CoverUriConverter() });
        coverImg.SetBinding(VisualElement.IsVisibleProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.HasCover)}"));

        var coverPlaceholder = new Border
        {
            HeightRequest = 200,
            WidthRequest = 200,
            HorizontalOptions = LayoutOptions.Center,
            StrokeThickness = 0,
            Background = GetBrush("CardBackgroundStrongColor", "#2DFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18) },
            Content = new Label
            {
                FontSize = 56,
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            },
        };
        ((Label)coverPlaceholder.Content).SetBinding(Label.TextProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.CoverText)}"));
        ((Label)coverPlaceholder.Content).SetDynamicResource(Label.TextColorProperty, "PrimaryColor");
        coverPlaceholder.SetBinding(VisualElement.IsVisibleProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.HasCover)}")
            { Converter = new InvertBoolConverter() });

        var coverBox = new Grid
        {
            HeightRequest = 200,
            HorizontalOptions = LayoutOptions.Center,
            Children = { coverPlaceholder, coverImg },
        };

        // 歌词预览
        var lyricsLabel = NewLabel(14, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: false);
        lyricsLabel.LineBreakMode = LineBreakMode.WordWrap;
        lyricsLabel.SetBinding(Label.TextProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.PreviewLyrics)}"));

        var lyricsScroll = new ScrollView
        {
            Margin = new Thickness(0, 6, 0, 0),
            Content = lyricsLabel,
        };

        var lyricsHeader = NewLabel(14, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", false);
        lyricsHeader.Text = "歌词";

        // 写入按钮
        var applyButton = new Button
        {
            Text = "写入标签",
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = Text("PrimaryColor", "#8C7BFF"),
            CornerRadius = 22,
            HeightRequest = 48,
            Margin = new Thickness(0, 8, 0, 0),
        };
        applyButton.SetBinding(Button.CommandProperty, nameof(UnifiedSearchViewModel.ApplyCommand));
        applyButton.SetBinding(Button.IsEnabledProperty,
            new Binding(nameof(UnifiedSearchViewModel.IsBusy)) { Converter = new InverseBooleanConverter(), Source = _vm });

        var statusLabel = NewLabel(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: false);
        statusLabel.HorizontalOptions = LayoutOptions.Center;
        statusLabel.SetBinding(Label.TextProperty, nameof(UnifiedSearchViewModel.StatusText));

        var content = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(20, 16, 20, 24),
            Children =
            {
                titleLabel,
                subLabel,
                badge,
                coverBox,
                lyricsHeader,
                lyricsScroll,
                statusLabel,
                applyButton,
            },
        };

        return new Grid { Children = { content } };
    }

    // ── 小工具 ──
    private static Label NewLabel(double fontSize, FontAttributes weight, string key, string fallback, bool tail)
    {
        var label = new Label
        {
            FontSize = fontSize,
            FontAttributes = weight,
            LineBreakMode = tail ? LineBreakMode.TailTruncation : LineBreakMode.WordWrap,
        };
        label.SetDynamicResource(Label.TextColorProperty, key);
        _ = fallback;
        return label;
    }

    private static Color Text(string key, string fallback)
        => GetResourceColor(key, fallback);

    private static Brush GetBrush(string key, string fallback)
        => new SolidColorBrush(GetResourceColor(key, fallback));

    private static Color GetResourceColor(string key, string fallback)
        => Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c
            ? c
            : Color.FromArgb(fallback);
}