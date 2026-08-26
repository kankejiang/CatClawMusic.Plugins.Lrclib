using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 封面搜索补全页（Lyrico SearchCover 复刻）：
/// 顶部圆角搜索面板（歌名/艺人）→ iTunes 候选卡片列表 → 点卡片弹出底部预览面板 → 「写入标签」。
/// 纯 C# 构建，复用宿主全局主题资源（DynamicResource），缺失时回退默认色。
/// </summary>
public class SearchCoverPage : ContentPage
{
    private readonly SearchCoverViewModel _vm;
    private bool _searchExpanded;
    private View? _searchBarRow;
    private View? _searchPanel;
    private VerticalStackLayout? _searchContainer;

    public SearchCoverPage(SongItem song)
    {
        _vm = new SearchCoverViewModel(song, new ItunesApiClient(), PluginHost.AudioFiles);
        BindingContext = _vm;

        Title = "搜索封面";
        BackgroundColor = GetResourceColor("WindowBackgroundColor", "#1A1838");

        _searchBarRow = BuildCollapsedBar();
        _searchPanel = BuildExpandedPanel();
        _searchPanel.IsVisible = false;
        _searchContainer = new VerticalStackLayout { Spacing = 0, Children = { _searchBarRow, _searchPanel } };

        var list = BuildCandidatesList();

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

        // 底部封面预览面板（覆盖全页）：跨满两行，否则被约束在 row0 搜索卡区域内
        var sheet = BuildCandidateSheet();
        Grid.SetRowSpan(sheet, 2);
        content.Add(sheet, 0, 0);

        Content = content;
    }

    private bool _autoSearched;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 进入页面自动搜索一次当前歌曲（从编辑页跳转时免手动点搜索）
        if (!_autoSearched && _vm.SearchCommand.CanExecute(null))
        {
            _autoSearched = true;
            _vm.SearchCommand.Execute(null);
        }
    }

    // ── 顶部搜索栏（折叠态）──
    private View BuildCollapsedBar()
    {
        var keywordLabel = NewLabel(14, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", tail: true);
        keywordLabel.SetBinding(Label.TextProperty,
            new MultiBinding
            {
                Bindings =
                {
                    new Binding(nameof(SearchCoverViewModel.SearchTitle)),
                    new Binding(nameof(SearchCoverViewModel.SearchArtist)),
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
        editBtn.Clicked += (_, _) =>
        {
            _searchExpanded = !_searchExpanded;
            if (_searchPanel != null) _searchPanel.IsVisible = _searchExpanded;
        };

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

    // ── 顶部搜索面板（展开态）──
    private View BuildExpandedPanel()
    {
        var titleEntry = new Entry
        {
            Placeholder = "歌名（默认取当前歌曲标题）",
            TextColor = Text("TextPrimaryColor", "#F7F8FF"),
            PlaceholderColor = Text("TextSecondaryColor", "#C2C6E4"),
            BackgroundColor = Colors.Transparent,
        };
        titleEntry.SetBinding(Entry.TextProperty, nameof(SearchCoverViewModel.SearchTitle));

        var artistEntry = new Entry
        {
            Placeholder = "艺人（可空）",
            TextColor = Text("TextPrimaryColor", "#F7F8FF"),
            PlaceholderColor = Text("TextSecondaryColor", "#C2C6E4"),
            BackgroundColor = Colors.Transparent,
        };
        artistEntry.SetBinding(Entry.TextProperty, nameof(SearchCoverViewModel.SearchArtist));

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
        searchButton.SetBinding(Button.CommandProperty, nameof(SearchCoverViewModel.SearchCommand));
        searchButton.SetBinding(Button.IsEnabledProperty,
            new Binding(nameof(SearchCoverViewModel.IsBusy)) { Converter = new InverseBooleanConverter(), Source = _vm });

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
        statusLabel.SetBinding(Label.TextProperty, nameof(SearchCoverViewModel.StatusText));

        return new Border
        {
            Margin = new Thickness(12, 10, 12, 4),
            StrokeThickness = 0,
            Background = GetBrush("CardBackgroundColor", "#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Padding = new Thickness(12, 10),
            Content = new VerticalStackLayout { Spacing = 0, Children = { searchRow, statusLabel } },
        };
    }

    // ── 候选列表 ──
    private View BuildCandidatesList()
    {
        var list = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            Margin = new Thickness(0),
            ItemTemplate = new DataTemplate(CreateCandidateCard),
        };
        list.SetBinding(CollectionView.ItemsSourceProperty, nameof(SearchCoverViewModel.Candidates));
        return list;
    }

    // ── 候选卡片 ──
    private View CreateCandidateCard()
    {
        // 左侧封面图（有图显示图，无图显示首字占位）
        var cover = new Border
        {
            HeightRequest = 64,
            WidthRequest = 64,
            StrokeThickness = 0,
            Background = GetBrush("CardBackgroundStrongColor", "#2DFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            Content = new Label
            {
                FontSize = 22,
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            },
        };
        cover.Content.SetBinding(Label.TextProperty, nameof(CoverCandidate.CoverText));
        ((Label)cover.Content).SetDynamicResource(Label.TextColorProperty, "PrimaryColor");

        // 真实封面图覆盖在占位上
        var image = new Image
        {
            HeightRequest = 64,
            WidthRequest = 64,
            Aspect = Aspect.AspectFill,
        };
        image.SetBinding(Image.SourceProperty, new Binding(nameof(CoverCandidate.ThumbUrl))
        {
            Converter = new CoverUriConverter(),
        });
        image.SetBinding(Image.IsVisibleProperty, nameof(CoverCandidate.HasCover));

        var coverBox = new Grid
        {
            HeightRequest = 64,
            WidthRequest = 64,
            Children = { cover, image },
        };

        // 标题
        var titleLabel = NewLabel(15, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", tail: true);
        titleLabel.SetBinding(Label.TextProperty, nameof(CoverCandidate.DisplayTitle));
        titleLabel.MaxLines = 1;

        var subtitle = NewLabel(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: true);
        subtitle.SetBinding(Label.TextProperty, nameof(CoverCandidate.Subtitle));
        subtitle.MaxLines = 1;

        var textStack = new VerticalStackLayout
        {
            Spacing = 3,
            VerticalOptions = LayoutOptions.Center,
            Children = { titleLabel, subtitle },
        };

        var row = new Grid
        {
            ColumnSpacing = 12,
            Padding = new Thickness(12, 10),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };
        row.Add(coverBox, 0);
        row.Add(textStack, 1);

        var card = new Border
        {
            Margin = new Thickness(12, 3),
            StrokeThickness = 0,
            Opacity = 1.0,
            Background = GetBrush("CardBackgroundColor", "#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Content = row,
        };
        card.SetBinding(OpacityProperty, new Binding(nameof(CoverCandidate.HasCover))
        {
            Converter = new BoolToValueConverter(1.0, 0.45),
        });

        var tap = new TapGestureRecognizer();
        tap.SetBinding(TapGestureRecognizer.CommandParameterProperty, new Binding("."));
        tap.SetBinding(TapGestureRecognizer.CommandProperty, new Binding(
            nameof(SearchCoverViewModel.OpenCandidateCommand), source: _vm));
        card.GestureRecognizers.Add(tap);

        return card;
    }

    // ── 底部封面预览面板 ──
    private Grid BuildCandidateSheet()
    {
        var scrim = new BoxView { Color = Color.FromArgb("#8C000000") };
        var scrimTap = new TapGestureRecognizer();
        scrimTap.SetBinding(TapGestureRecognizer.CommandProperty, new Binding(nameof(SearchCoverViewModel.ClosePreviewCommand), source: _vm));
        scrim.GestureRecognizers.Add(scrimTap);

        var sheetTitle = NewLabel(18, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", tail: true);
        sheetTitle.SetBinding(Label.TextProperty, new Binding($"{nameof(SearchCoverViewModel.Selected)}.{nameof(CoverCandidate.DisplayTitle)}"));
        sheetTitle.MaxLines = 1;

        var sheetSubtitle = NewLabel(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: true);
        sheetSubtitle.SetBinding(Label.TextProperty, new Binding($"{nameof(SearchCoverViewModel.Selected)}.{nameof(CoverCandidate.Subtitle)}"));
        sheetSubtitle.MaxLines = 1;

        // 高清大图预览
        var previewImage = new Image
        {
            HeightRequest = 260,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center,
        };
        previewImage.SetBinding(Image.SourceProperty, new Binding($"{nameof(SearchCoverViewModel.Selected)}.{nameof(CoverCandidate.HighResUrl)}")
        {
            Converter = new CoverUriConverter(),
        });

        var applyButton = new Button
        {
            Text = "写入标签",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = Text("PrimaryColor", "#8C7BFF"),
            CornerRadius = 20,
            HeightRequest = 40,
        };
        applyButton.SetBinding(Button.CommandProperty, nameof(SearchCoverViewModel.WriteCoverCommand));

        var panel = new Border
        {
            StrokeThickness = 0,
            Background = GetBrush("CardBackgroundStrongColor", "#2DFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(0, 0, 0, 0) },
            Padding = new Thickness(20, 16, 20, 24),
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children = { sheetTitle, sheetSubtitle, previewImage, applyButton },
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
        sheet.SetBinding(Grid.IsVisibleProperty, nameof(SearchCoverViewModel.ShowPreview));

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

/// <summary>http(s) 封面 URL → ImageSource；空返回 null</summary>
internal class CoverUriConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s)) return null;
        try { return ImageSource.FromUri(new Uri(s)); }
        catch { return null; }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
