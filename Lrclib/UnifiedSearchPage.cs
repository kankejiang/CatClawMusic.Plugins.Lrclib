using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 统一搜索页：同时搜索歌词（LRCLIB + Lyrico 多源）和封面（iTunes），
/// 结果合并展示（封面缩略图 + 标题/艺人 + 歌词/封面标记），
/// 点击结果直接写入标签（歌词 + 封面）。
/// </summary>
public class UnifiedSearchPage : ContentPage
{
    private readonly UnifiedSearchViewModel _vm;
    private bool _searchExpanded;
    private View? _searchBarRow;
    private View? _searchPanel;
    private VerticalStackLayout? _searchContainer;

    public UnifiedSearchPage(SongItem song)
    {
        _vm = new UnifiedSearchViewModel(song, new LrclibApiClient(), new ItunesApiClient(),
            PluginHost.AudioFiles, PluginHost.LyricoHub);
        BindingContext = _vm;

        Title = "搜索补全";
        BackgroundColor = GetResourceColor("WindowBackgroundColor", "#1A1838");

        _searchBarRow = BuildCollapsedBar();
        _searchPanel = BuildExpandedPanel();
        _searchPanel.IsVisible = false;
        _searchContainer = new VerticalStackLayout { Spacing = 0, Children = { _searchBarRow, _searchPanel } };

        var list = BuildResultList();

        var content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
        };
        content.Add(_searchContainer, 0, 0);
        content.Add(list, 0, 1);

        var sheet = BuildPreviewSheet();
        Grid.SetRowSpan(sheet, 2);
        content.Add(sheet, 0, 0);

        Content = content;
    }

    private bool _autoSearched;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_autoSearched && _vm.SearchCommand.CanExecute(null))
        {
            _autoSearched = true;
            _vm.SearchCommand.Execute(null);
        }
    }

    // ── 折叠搜索栏 ──
    private View BuildCollapsedBar()
    {
        var keywordLabel = NewLabel(14, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", tail: true);
        keywordLabel.SetBinding(Label.TextProperty,
            new MultiBinding
            {
                Bindings =
                {
                    new Binding(nameof(UnifiedSearchViewModel.SearchTitle)),
                    new Binding(nameof(UnifiedSearchViewModel.SearchArtist)),
                },
                Converter = new CollapsedKeywordConverter(),
            });
        keywordLabel.VerticalOptions = LayoutOptions.Center;

        var editBtn = new Button
        {
            Text = "修改",
            FontSize = 13,
            TextColor = Text("TextPrimaryColor", "#F7F8FF"),
            BackgroundColor = Colors.Transparent,
            HeightRequest = 32,
            Padding = new Thickness(10, 0),
            HorizontalOptions = LayoutOptions.End,
        };
        editBtn.Clicked += (_, _) => ToggleSearchPanel();

        var row = new Grid
        {
            Padding = new Thickness(16, 8, 12, 6),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        row.Add(keywordLabel, 0, 0);
        row.Add(editBtn, 1, 0);
        return row;
    }

    private void ToggleSearchPanel()
    {
        _searchExpanded = !_searchExpanded;
        if (_searchPanel != null) _searchPanel.IsVisible = _searchExpanded;
    }

    // ── 展开搜索面板 ──
    private View BuildExpandedPanel()
    {
        var titleEntry = new Entry
        {
            Placeholder = "歌名",
            TextColor = Text("TextPrimaryColor", "#F7F8FF"),
            PlaceholderColor = Text("TextSecondaryColor", "#C2C6E4"),
            BackgroundColor = Colors.Transparent,
        };
        titleEntry.SetBinding(Entry.TextProperty, nameof(UnifiedSearchViewModel.SearchTitle));

        var artistEntry = new Entry
        {
            Placeholder = "艺人（可空）",
            TextColor = Text("TextPrimaryColor", "#F7F8FF"),
            PlaceholderColor = Text("TextSecondaryColor", "#C2C6E4"),
            BackgroundColor = Colors.Transparent,
        };
        artistEntry.SetBinding(Entry.TextProperty, nameof(UnifiedSearchViewModel.SearchArtist));

        var searchButton = new Button
        {
            Text = "搜索",
            FontSize = 14,
            TextColor = Colors.White,
            BackgroundColor = Text("PrimaryColor", "#8C7BFF"),
            CornerRadius = 18,
            Padding = new Thickness(16, 2),
            HeightRequest = 36,
        };
        searchButton.SetBinding(Button.CommandProperty, nameof(UnifiedSearchViewModel.SearchCommand));
        searchButton.SetBinding(Button.IsEnabledProperty,
            new Binding(nameof(UnifiedSearchViewModel.IsBusy)) { Converter = new InverseBooleanConverter(), Source = _vm });

        var searchRow = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        searchRow.Add(titleEntry, 0);
        searchRow.Add(artistEntry, 1);
        searchRow.Add(searchButton, 2);

        var statusLabel = NewLabel(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: false);
        statusLabel.LineBreakMode = LineBreakMode.WordWrap;
        statusLabel.Margin = new Thickness(2, 6, 2, 0);
        statusLabel.SetBinding(Label.TextProperty, nameof(UnifiedSearchViewModel.StatusText));

        return new Border
        {
            Margin = new Thickness(12, 2, 12, 4),
            StrokeThickness = 0,
            Background = GetBrush("CardBackgroundColor", "#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Padding = new Thickness(12, 10),
            Content = new VerticalStackLayout { Spacing = 0, Children = { searchRow, statusLabel } },
        };
    }

    // ── 结果列表 ──
    private View BuildResultList()
    {
        var list = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            Margin = new Thickness(12, 4, 12, 0),
            ItemTemplate = new DataTemplate(BuildResultCard),
        };
        list.SelectionChanged += (s, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is not UnifiedSearchResult item) return;
            _vm.OpenPreviewCommand.Execute(item);
            list.SelectedItem = null; // 清除选中，允许再次点击同一条
        };
        list.SetBinding(ItemsView.ItemsSourceProperty, nameof(UnifiedSearchViewModel.Results));
        return list;
    }

    private View BuildResultCard()
    {
        // 封面缩略图
        var cover = new Image
        {
            HeightRequest = 56,
            WidthRequest = 56,
            Aspect = Aspect.AspectFill,
        };
        cover.SetBinding(Image.SourceProperty, nameof(UnifiedSearchResult.CoverUrl),
            converter: new CoverUriConverter());
        cover.SetBinding(VisualElement.IsVisibleProperty, nameof(UnifiedSearchResult.HasCover));

        var placeholderLabel = new Label
        {
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        placeholderLabel.SetBinding(Label.TextProperty, nameof(UnifiedSearchResult.CoverText));
        placeholderLabel.SetDynamicResource(Label.TextColorProperty, "PrimaryColor");

        var placeholder = new Border
        {
            HeightRequest = 56,
            WidthRequest = 56,
            StrokeThickness = 0,
            Background = GetBrush("CardBackgroundStrongColor", "#2DFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            Content = placeholderLabel,
        };
        placeholder.SetBinding(VisualElement.IsVisibleProperty,
            new Binding(nameof(UnifiedSearchResult.HasCover)) { Converter = new InvertBoolConverter() });

        var coverBox = new Grid
        {
            HeightRequest = 56,
            WidthRequest = 56,
            HorizontalOptions = LayoutOptions.Start,
            Children = { placeholder, cover },
        };

        // 标题
        var titleLabel = NewLabel(15, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", tail: true);
        titleLabel.SetBinding(Label.TextProperty, nameof(UnifiedSearchResult.DisplayTitle));
        titleLabel.MaxLines = 1;

        // 副标题（专辑 · 时长）
        var subLabel = NewLabel(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: true);
        subLabel.SetBinding(Label.TextProperty, nameof(UnifiedSearchResult.Subtitle));

        // 来源 / 能力徽标
        var badge = NewLabel(10, FontAttributes.Bold, "TextSecondaryColor", "#C2C6E4", false);
        badge.SetBinding(Label.TextProperty, nameof(UnifiedSearchResult.Badge));
        badge.SetDynamicResource(Label.TextColorProperty, "PrimaryColor");

        var textStack = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { titleLabel, subLabel, badge },
        };

        var row = new Grid
        {
            Padding = new Thickness(0, 6),
            ColumnSpacing = 12,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };
        row.Add(coverBox, 0, 0);
        row.Add(textStack, 1, 0);

        var card = new Border
        {
            StrokeThickness = 0,
            Background = GetBrush("CardBackgroundColor", "#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Padding = new Thickness(12, 8),
            Margin = new Thickness(0, 0, 0, 8),
            Content = row,
        };

        return card;
    }

    // ── 底部预览面板（封面 + 歌词）──
    private Grid BuildPreviewSheet()
    {
        var scrim = new BoxView { Color = Color.FromArgb("#8C000000") };
        var scrimTap = new TapGestureRecognizer();
        scrimTap.Tapped += (_, _) => _vm.ClosePreviewCommand.Execute(null);
        scrim.GestureRecognizers.Add(scrimTap);

        // 标题
        var sheetTitle = NewLabel(17, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", tail: true);
        sheetTitle.SetBinding(Label.TextProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.DisplayTitle)}"));
        sheetTitle.MaxLines = 1;

        var sheetSubtitle = NewLabel(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: true);
        sheetSubtitle.SetBinding(Label.TextProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.Subtitle)}"));

        // 封面预览图
        var coverImg = new Image
        {
            HeightRequest = 160,
            WidthRequest = 160,
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
            HeightRequest = 160,
            WidthRequest = 160,
            HorizontalOptions = LayoutOptions.Center,
            StrokeThickness = 0,
            Background = GetBrush("CardBackgroundStrongColor", "#2DFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Content = new Label
            {
                FontSize = 48,
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            }
        };
        ((Label)coverPlaceholder.Content).SetBinding(Label.TextProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.CoverText)}"));
        ((Label)coverPlaceholder.Content).SetDynamicResource(Label.TextColorProperty, "PrimaryColor");
        coverPlaceholder.SetBinding(VisualElement.IsVisibleProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.HasCover)}")
            { Converter = new InvertBoolConverter() });

        var coverBox = new Grid
        {
            HeightRequest = 160,
            HorizontalOptions = LayoutOptions.Center,
            Children = { coverPlaceholder, coverImg },
        };

        // 歌词预览
        var lyricsLabel = NewLabel(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: false);
        lyricsLabel.LineBreakMode = LineBreakMode.NoWrap;
        lyricsLabel.VerticalOptions = LayoutOptions.Start;
        lyricsLabel.SetBinding(Label.TextProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.PreviewLyrics)}"));

        var lyricsScroll = new ScrollView
        {
            MaximumHeightRequest = 180,
            Content = lyricsLabel,
        };

        var lyricsHeader = NewLabel(13, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", false);
        lyricsHeader.Text = "歌词预览";

        // 写入按钮
        var applyButton = new Button
        {
            Text = "写入标签",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = Text("PrimaryColor", "#8C7BFF"),
            CornerRadius = 20,
            HeightRequest = 44,
        };
        applyButton.SetBinding(Button.CommandProperty, nameof(UnifiedSearchViewModel.ApplyCommand));
        applyButton.SetBinding(Button.IsEnabledProperty,
            new Binding(nameof(UnifiedSearchViewModel.IsBusy)) { Converter = new InverseBooleanConverter(), Source = _vm });

        var panel = new Border
        {
            StrokeThickness = 0,
            Background = GetBrush("WindowBackgroundColor", "#FF2A254E"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20, 20, 0, 0) },
            Padding = new Thickness(20, 16, 20, 24),
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    sheetTitle,
                    sheetSubtitle,
                    coverBox,
                    lyricsHeader,
                    lyricsScroll,
                    applyButton,
                },
            },
        };

        var sheet = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
        };
        sheet.Add(scrim, 0, 0);
        sheet.Add(panel, 0, 1);
        sheet.BindingContext = _vm;
        sheet.SetBinding(Grid.IsVisibleProperty, nameof(UnifiedSearchViewModel.ShowPreview));

        return sheet;
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
