using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 统一搜索页：同时搜索歌词（LRCLIB + Lyrico 多源）和封面（iTunes），
/// 结果合并展示（封面缩略图 + 标题/艺人 + 歌词/封面标记），
/// 点击结果用系统页面导航进入预览页，一键写入歌词 + 封面。
/// </summary>
public class UnifiedSearchPage : ContentPage
{
    private readonly UnifiedSearchViewModel _vm;
    private HorizontalStackLayout? _chipStack;

    public UnifiedSearchPage(SongItem song)
    {
        _vm = new UnifiedSearchViewModel(song, new LrclibApiClient(), new ItunesApiClient(),
            PluginHost.AudioFiles, PluginHost.LyricoHub);
        BindingContext = _vm;

        Title = "搜索补全";
        BackgroundColor = GetResourceColor("WindowBackgroundColor", "#1A1838");

        var searchBar = BuildSearchBar();
        var filterSection = BuildFilterSection();
        var list = BuildResultList();

        _vm.SourceFilters.CollectionChanged += (_, _) => RebuildChips();

        var contentGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
        };
        contentGrid.Add(searchBar, 0, 0);
        contentGrid.Add(filterSection, 0, 1);
        contentGrid.Add(list, 0, 2);

        Content = contentGrid;
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

    // ── 搜索栏（常驻展开态：胶囊输入框 + 紫色搜索按钮）──
    private View BuildSearchBar()
    {
        var entry = new Entry
        {
            Placeholder = "搜索歌词 / 封面",
            TextColor = Text("TextPrimaryColor", "#F7F8FF"),
            PlaceholderColor = Text("TextSecondaryColor", "#C2C6E4"),
            BackgroundColor = Colors.Transparent,
            FontSize = 14,
            HeightRequest = 40,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
        };
        entry.SetBinding(Entry.TextProperty, nameof(UnifiedSearchViewModel.SearchTitle));

        var searchBtn = new Button
        {
            Text = "搜索",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = Text("PrimaryColor", "#8C7BFF"),
            CornerRadius = 20,
            HeightRequest = 36,
            Padding = new Thickness(16, 0),
        };
        searchBtn.SetBinding(Button.CommandProperty, nameof(UnifiedSearchViewModel.SearchCommand));
        searchBtn.SetBinding(Button.IsEnabledProperty,
            new Binding(nameof(UnifiedSearchViewModel.IsBusy)) { Converter = new InverseBooleanConverter(), Source = _vm });

        var bar = new Border
        {
            Margin = new Thickness(16, 12, 16, 4),
            StrokeThickness = 0,
            Background = GetBrush("CardBackgroundColor", "#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(22) },
            Padding = new Thickness(16, 2, 6, 2),
            Content = new Grid
            {
                ColumnSpacing = 8,
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                },
                Children = { entry, searchBtn },
            },
        };
        Grid.SetColumn(entry, 0);
        Grid.SetColumn(searchBtn, 1);
        return bar;
    }

    // ── 来源筛选区：标签 + chip 行 ──
    private View BuildFilterSection()
    {
        var label = new Label
        {
            Text = "按来源筛选",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = Text("TextSecondaryColor", "#C2C6E4"),
            Margin = new Thickness(16, 6, 16, 4),
        };

        _chipStack = new HorizontalStackLayout { Spacing = 8 };
        var scroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            Padding = new Thickness(16, 0, 16, 8),
            Content = _chipStack,
        };

        return new VerticalStackLayout
        {
            Spacing = 0,
            Children = { label, scroll },
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
            _ = PluginNav.PushAsync(new UnifiedSearchPreviewPage(_vm));
            list.SelectedItem = null; // 清除选中，允许再次点击同一条
        };
        list.SetBinding(ItemsView.ItemsSourceProperty, nameof(UnifiedSearchViewModel.FilteredResults));
        return list;
    }

    // ── 来源筛选 chips ──
    private View BuildChipLabel()
        => new Label
        {
            Text = "按来源筛选",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(16, 6, 16, 2),
            TextColor = Text("TextSecondaryColor", "#C2C6E4"),
        };

    private View BuildChipRow()
    {
        _chipStack = new HorizontalStackLayout { Spacing = 8, Padding = new Thickness(16, 0, 16, 8) };
        return new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = _chipStack,
        };
    }

    private void RebuildChips()
    {
        if (_chipStack == null) return;
        _chipStack.Children.Clear();
        foreach (var f in _vm.SourceFilters)
        {
            var chip = new Button
            {
                Text = f.Label,
                FontSize = 12,
                TextColor = f.IsActive ? Colors.White : Text("TextSecondaryColor", "#C2C6E4"),
                BackgroundColor = f.IsActive ? Text("PrimaryColor", "#8C7BFF") : Color.FromArgb("#1AFFFFFF"),
                CornerRadius = 16,
                HeightRequest = 30,
                Padding = new Thickness(14, 0),
            };
            var captured = f;
            chip.Clicked += (_, _) =>
            {
                _vm.SelectSourceCommand.Execute(captured);
                RebuildChips();
            };
            _chipStack.Children.Add(chip);
        }
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
