using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 统一搜索单条结果预览页（对齐原型）：
/// 顶部大封面 + 歌名/副标题/徽标 → 三 Tab 切换（歌词 / 封面 / 元数据）
/// → 右上角「写入」按钮，一键写入歌词 + 封面。
/// 用系统页面导航承载，不依赖自绘定位。
/// </summary>
public sealed class UnifiedSearchPreviewPage : ContentPage
{
    private readonly UnifiedSearchViewModel _vm;
    private int _activeTab;
    private ContentView? _contentView;
    private Grid? _tabsGrid;

    public UnifiedSearchPreviewPage(UnifiedSearchViewModel vm)
    {
        _vm = vm;
        BindingContext = _vm;

        Title = "结果预览";
        BackgroundColor = GetResourceColor("WindowBackgroundColor", "#1A1838");

        var header = BuildHeader();
        _tabsGrid = BuildTabs();
        _contentView = new ContentView { Content = BuildLyricsTab() };

        var rootGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
        };
        rootGrid.Add(header, 0, 0);
        rootGrid.Add(_tabsGrid, 0, 1);
        rootGrid.Add(_contentView, 0, 2);

        Content = rootGrid;

        _vm.Applied += async (_, _) =>
        {
            try { await PluginNav.PopAsync(); } catch { }
        };
    }

    // ── 顶部：封面 + 歌名 + 副标题 + 徽标 + 写入按钮 ──
    private View BuildHeader()
    {
        // 封面
        var coverImg = new Image
        {
            HeightRequest = 140,
            WidthRequest = 140,
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
            HeightRequest = 140,
            WidthRequest = 140,
            HorizontalOptions = LayoutOptions.Center,
            StrokeThickness = 0,
            Background = GetBrush("CardBackgroundStrongColor", "#2DFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Content = new Label
            {
                FontSize = 44,
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
            HeightRequest = 140,
            HorizontalOptions = LayoutOptions.Center,
            Children = { coverPlaceholder, coverImg },
        };

        // 标题
        var title = new Label
        {
            FontSize = 17,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
        };
        title.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");
        title.SetBinding(Label.TextProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.DisplayTitle)}"));

        // 副标题
        var subtitle = new Label
        {
            FontSize = 12,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
        };
        subtitle.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
        subtitle.SetBinding(Label.TextProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.Subtitle)}"));

        // 徽标（用 Border 实现胶囊形，Label 无 CornerRadius）
        var badgeStack = new HorizontalStackLayout
        {
            Spacing = 6,
            HorizontalOptions = LayoutOptions.Center,
        };

        Border MakeBadge(string textKey, string bgHex, string textRes, string textFallback,
            string? visiblePath = null, Binding? textBinding = null)
        {
            var label = new Label
            {
                FontSize = 10,
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            };
            label.SetDynamicResource(Label.TextColorProperty, textRes);
            if (textBinding != null) label.SetBinding(Label.TextProperty, textBinding);
            else label.Text = textKey;

            var border = new Border
            {
                StrokeThickness = 0,
                BackgroundColor = Color.FromArgb(bgHex),
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                Padding = new Thickness(8, 2),
                Content = label,
            };
            if (visiblePath != null)
                border.SetBinding(VisualElement.IsVisibleProperty, visiblePath);
            return border;
        }

        var lyricsBadge = MakeBadge(
            "", "#268C7BFF", "PrimaryColor", "#A99BFF",
            visiblePath: $"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.HasLyrics)}",
            textBinding: new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.LyricsType)}"));

        var coverBadge = MakeBadge(
            "封面", "#264ADE80", "TextPrimaryColor", "#4ADE80",
            visiblePath: $"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.HasCover)}");
        // 绿色文字
        ((Label)((Border)coverBadge).Content).TextColor = Color.FromArgb("#4ADE80");
        ((Label)((Border)coverBadge).Content).SetDynamicResource(Label.TextColorProperty, "PrimaryColor");

        var sourceBadge = MakeBadge(
            "", "#2DFFFFFF", "TextSecondaryColor", "#C2C6E4",
            textBinding: new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.Source)}")
            { Converter = new SourceLabelConverter() });

        badgeStack.Children.Add(lyricsBadge);
        badgeStack.Children.Add(coverBadge);
        badgeStack.Children.Add(sourceBadge);

        // 写入按钮（右上角）
        var applyBtn = new Button
        {
            Text = "写入",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = GetResourceColor("PrimaryColor", "#8C7BFF"),
            CornerRadius = 16,
            HeightRequest = 32,
            Padding = new Thickness(14, 0),
            HorizontalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 16, 16, 0),
        };
        applyBtn.SetBinding(Button.CommandProperty, nameof(UnifiedSearchViewModel.ApplyCommand));
        applyBtn.SetBinding(Button.IsEnabledProperty,
            new Binding(nameof(UnifiedSearchViewModel.IsBusy)) { Converter = new InverseBooleanConverter(), Source = _vm });

        // 状态文字
        var statusLabel = new Label
        {
            FontSize = 11,
            HorizontalTextAlignment = TextAlignment.Center,
        };
        statusLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
        statusLabel.SetBinding(Label.TextProperty, nameof(UnifiedSearchViewModel.StatusText));

        // 写入勾选：元数据 / 歌词 / 封面
        var checkboxRow = new Grid
        {
            ColumnSpacing = 4,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };

        void AddCheckbox(string label, string valueProp, int col)
        {
            var cb = new CheckBox
            {
                VerticalOptions = LayoutOptions.Center,
                Scale = 0.8,
                Color = GetResourceColor("PrimaryColor", "#8C7BFF"),
                IsChecked = col switch
                {
                    0 => _vm.ApplyMetadata,
                    2 => _vm.ApplyLyrics,
                    _ => _vm.ApplyCover,
                },
            };
            cb.SetBinding(CheckBox.IsCheckedProperty,
                new Binding(valueProp, source: _vm));

            var lb = new Label
            {
                Text = label,
                FontSize = 12,
                VerticalOptions = LayoutOptions.Center,
                TextColor = GetResourceColor("TextSecondaryColor", "#C2C6E4"),
            };

            checkboxRow.Add(cb, col, 0);
            checkboxRow.Add(lb, col + 1, 0);
        }

        AddCheckbox("元数据", nameof(UnifiedSearchViewModel.ApplyMetadata), 0);
        AddCheckbox("歌词", nameof(UnifiedSearchViewModel.ApplyLyrics), 2);
        AddCheckbox("封面", nameof(UnifiedSearchViewModel.ApplyCover), 4);

        var stack = new VerticalStackLayout
        {
            Spacing = 8,
            Padding = new Thickness(20, 16, 20, 12),
            Children =
            {
                coverBox,
                title,
                subtitle,
                badgeStack,
                checkboxRow,
                statusLabel,
            },
        };

        // 写入按钮浮在右上角
        var outer = new Grid();
        outer.Children.Add(stack);
        outer.Children.Add(applyBtn);
        return outer;
    }

    // ── Tab 切换条 ──
    private Grid BuildTabs()
    {
        var tabs = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
            HeightRequest = 40,
        };

        var labels = new[] { "歌词", "封面", "元数据" };
        var labelViews = new Label[3];

        for (int i = 0; i < 3; i++)
        {
            var idx = i;
            var label = new Label
            {
                Text = labels[i],
                FontSize = 14,
                FontAttributes = idx == 0 ? FontAttributes.Bold : FontAttributes.None,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                TextColor = idx == 0
                    ? GetResourceColor("TextPrimaryColor", "#F7F8FF")
                    : GetResourceColor("TextSecondaryColor", "#8E93B8"),
            };
            labelViews[i] = label;

            var tap = new TapGestureRecognizer();
            var captured = idx;
            tap.Tapped += (_, _) => SwitchTab(captured, labelViews);
            label.GestureRecognizers.Add(tap);

            var cell = new Grid();
            cell.Children.Add(label);

            if (idx == 0)
            {
                var indicator = new BoxView
                {
                    HeightRequest = 2,
                    Color = GetResourceColor("PrimaryColor", "#8C7BFF"),
                    CornerRadius = 1,
                    WidthRequest = 24,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.End,
                    Margin = new Thickness(0, 0, 0, 4),
                    StyleId = "tab-indicator",
                };
                cell.Children.Add(indicator);
            }

            tabs.Add(cell, i, 0);
        }

        // 底部分隔线
        var sep = new BoxView
        {
            HeightRequest = 1,
            Color = Color.FromArgb("#1AFFFFFF"),
            VerticalOptions = LayoutOptions.End,
        };
        Grid.SetColumnSpan(sep, 3);
        tabs.Children.Add(sep);

        return tabs;
    }

    private void SwitchTab(int idx, Label[] labelViews)
    {
        _activeTab = idx;

        for (int i = 0; i < labelViews.Length; i++)
        {
            labelViews[i].FontAttributes = i == idx ? FontAttributes.Bold : FontAttributes.None;
            labelViews[i].TextColor = i == idx
                ? GetResourceColor("TextPrimaryColor", "#F7F8FF")
                : GetResourceColor("TextSecondaryColor", "#8E93B8");
        }

        // 移动指示条
        if (_tabsGrid != null)
        {
            var indicator = _tabsGrid.Children.OfType<Element>().FirstOrDefault(c => c.StyleId == "tab-indicator");
            if (indicator != null)
                Grid.SetColumn(indicator, idx);
        }

        // 切换内容
        if (_contentView != null)
        {
            _contentView.Content = idx switch
            {
                0 => BuildLyricsTab(),
                1 => BuildCoverTab(),
                2 => BuildMetadataTab(),
                _ => BuildLyricsTab(),
            };
        }
    }

    // ── Tab 1：歌词 ──
    private View BuildLyricsTab()
    {
        var label = new Label
        {
            FontSize = 14,
            LineHeight = 1.8,
            HorizontalTextAlignment = TextAlignment.Center,
        };
        label.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
        label.SetBinding(Label.TextProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.PreviewLyrics)}"));

        return new ScrollView
        {
            Padding = new Thickness(20, 16, 20, 20),
            Content = label,
        };
    }

    // ── Tab 2：封面 ──
    private View BuildCoverTab()
    {
        var img = new Image
        {
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
        };
        img.SetBinding(Image.SourceProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.HighResCoverUrl)}")
            { Converter = new CoverUriConverter() });
        img.SetBinding(VisualElement.IsVisibleProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.HasCover)}"));

        var placeholder = new Label
        {
            Text = "该结果无封面",
            FontSize = 14,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        placeholder.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
        placeholder.SetBinding(VisualElement.IsVisibleProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.HasCover)}")
            { Converter = new InvertBoolConverter() });

        return new Grid
        {
            Padding = new Thickness(20, 16),
            Children = { placeholder, img },
        };
    }

    // ── Tab 3：元数据 ──
    private View BuildMetadataTab()
    {
        var rows = new VerticalStackLayout { Spacing = 0 };

        void AddRow(string label, string valuePath)
        {
            var key = new Label { Text = label, FontSize = 12 };
            key.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");

            var val = new Label
            {
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                LineBreakMode = LineBreakMode.TailTruncation,
            };
            val.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");
            val.SetBinding(Label.TextProperty,
                new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{valuePath}"));

            var row = new StackLayout
            {
                Spacing = 2,
                Padding = new Thickness(20, 10, 20, 10),
                Children = { key, val },
            };
            rows.Children.Add(row);

            var sep = new BoxView { HeightRequest = 1, Color = Color.FromArgb("#1AFFFFFF") };
            sep.Margin = new Thickness(20, 0, 20, 0);
            rows.Children.Add(sep);
        }

        AddRow("歌名", nameof(UnifiedSearchResult.Title));
        AddRow("艺人", nameof(UnifiedSearchResult.Artist));
        AddRow("专辑", nameof(UnifiedSearchResult.Album));
        AddRow("时长", nameof(UnifiedSearchResult.Duration));
        AddRow("来源", nameof(UnifiedSearchResult.Source));

        return new ScrollView { Content = rows };
    }

    // ── 小工具 ──
    private static Color GetResourceColor(string key, string fallback)
        => Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c
            ? c
            : Color.FromArgb(fallback);

    private static Brush GetBrush(string key, string fallback)
        => new SolidColorBrush(GetResourceColor(key, fallback));
}

/// <summary>来源标识 → 展示名：iTunes → 苹果。</summary>
internal sealed class SourceLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var s = value?.ToString() ?? "";
        return string.Equals(s, "iTunes", StringComparison.OrdinalIgnoreCase) ? "苹果" : s;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}