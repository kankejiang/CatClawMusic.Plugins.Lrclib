using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 歌词搜索补全页（Lyrico SearchLyrics 复刻）：
/// 顶部圆角搜索面板（歌名/艺人）→ LRCLIB 候选卡片列表 → 点卡片弹出底部预览面板 → 「写入标签」。
/// 纯 C# 构建，复用宿主全局主题资源（DynamicResource），缺失时回退默认色。
/// </summary>
public class SearchLyricsPage : ContentPage
{
    private readonly SearchLyricsViewModel _vm;
    private bool _searchExpanded;
    private View? _searchBarRow;     // 折叠态：关键字 + 编辑按钮
    private View? _searchPanel;     // 展开态：输入框 + 搜索按钮
    private VerticalStackLayout? _searchContainer;

    public SearchLyricsPage(SongItem song)
    {
        _vm = new SearchLyricsViewModel(song, new LrclibApiClient(), PluginHost.AudioFiles, PluginHost.LyricoHub);
        BindingContext = _vm;

        Title = "搜索歌词";
        BackgroundColor = GetResourceColor("WindowBackgroundColor", "#1A1838");

        _searchBarRow = BuildCollapsedBar();
        _searchPanel = BuildExpandedPanel();
        _searchPanel.IsVisible = false;  // 默认折叠
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

        // 底部歌词预览面板（覆盖全页）：跨满两行，否则被约束在 row0 搜索卡区域内
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

    // ── 顶部搜索栏（折叠态：仅显示关键字 + 编辑按钮，节省空间）──
    private View BuildCollapsedBar()
    {
        var keywordLabel = NewLabel(14, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", tail: true);
        keywordLabel.SetBinding(Label.TextProperty,
            new MultiBinding
            {
                Bindings =
                {
                    new Binding(nameof(SearchLyricsViewModel.SearchTitle)),
                    new Binding(nameof(SearchLyricsViewModel.SearchArtist)),
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

    // ── 顶部搜索面板（展开态：歌名/艺人输入框 + 搜索按钮）──
    private View BuildExpandedPanel()
    {
        var titleEntry = new Entry
        {
            Placeholder = "歌名（默认取当前歌曲标题）",
            TextColor = Text("TextPrimaryColor", "#F7F8FF"),
            PlaceholderColor = Text("TextSecondaryColor", "#C2C6E4"),
            BackgroundColor = Colors.Transparent,
        };
        titleEntry.SetBinding(Entry.TextProperty, nameof(SearchLyricsViewModel.SearchTitle));

        var artistEntry = new Entry
        {
            Placeholder = "艺人（可空）",
            TextColor = Text("TextPrimaryColor", "#F7F8FF"),
            PlaceholderColor = Text("TextSecondaryColor", "#C2C6E4"),
            BackgroundColor = Colors.Transparent,
        };
        artistEntry.SetBinding(Entry.TextProperty, nameof(SearchLyricsViewModel.SearchArtist));

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
        searchButton.SetBinding(Button.CommandProperty, nameof(SearchLyricsViewModel.SearchCommand));
        searchButton.SetBinding(Button.IsEnabledProperty,
            new Binding(nameof(SearchLyricsViewModel.IsBusy)) { Converter = new InverseBooleanConverter(), Source = _vm });

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
        statusLabel.SetBinding(Label.TextProperty, nameof(SearchLyricsViewModel.StatusText));

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
        list.SetBinding(CollectionView.ItemsSourceProperty, nameof(SearchLyricsViewModel.Candidates));
        return list;
    }

    // ── 候选卡片（Lyrico SearchResultItem 风格）──
    private View CreateCandidateCard()
    {
        // 左侧封面色块占位
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
        cover.Content.SetBinding(Label.TextProperty, nameof(CandidateItem.CoverText));
        ((Label)cover.Content).SetDynamicResource(Label.TextColorProperty, "PrimaryColor");

        // 标题 + 徽标
        var titleLabel = NewLabel(15, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", tail: true);
        titleLabel.SetBinding(Label.TextProperty, nameof(CandidateItem.DisplayTitle));
        titleLabel.MaxLines = 1;

        var badge = BuildBadge();
        badge.SetBinding(Label.TextProperty, nameof(CandidateItem.Badge));
        badge.SetBinding(Label.TextColorProperty, new Binding(nameof(CandidateItem.HasLyrics))
        {
            Converter = new BoolToColorConverter(Text("PrimaryColor", "#8C7BFF"), Text("TextSecondaryColor", "#C2C6E4")),
        });

        var titleRow = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        titleRow.Add(titleLabel, 0);
        titleRow.Add(badge, 1);

        var subtitle = NewLabel(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: true);
        subtitle.SetBinding(Label.TextProperty, nameof(CandidateItem.Subtitle));
        subtitle.MaxLines = 1;

        var textStack = new VerticalStackLayout
        {
            Spacing = 3,
            VerticalOptions = LayoutOptions.Center,
            Children = { titleRow, subtitle },
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
        row.Add(cover, 0);
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
        card.SetBinding(OpacityProperty, new Binding(nameof(CandidateItem.HasLyrics))
        {
            Converter = new BoolToValueConverter(1.0, 0.45),
        });

        var tap = new TapGestureRecognizer();
        tap.SetBinding(TapGestureRecognizer.CommandParameterProperty, new Binding("."));
        tap.SetBinding(TapGestureRecognizer.CommandProperty, new Binding(
            nameof(SearchLyricsViewModel.OpenCandidateCommand), source: _vm));
        card.GestureRecognizers.Add(tap);

        return card;
    }

    // ── 底部歌词预览面板 ──
    private Grid BuildCandidateSheet()
    {
        var scrim = new BoxView { Color = Color.FromArgb("#8C000000") };
        var scrimTap = new TapGestureRecognizer();
        scrimTap.SetBinding(TapGestureRecognizer.CommandProperty, new Binding(nameof(SearchLyricsViewModel.ClosePreviewCommand), source: _vm));
        scrim.GestureRecognizers.Add(scrimTap);

        var sheetTitle = NewLabel(18, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", tail: true);
        sheetTitle.SetBinding(Label.TextProperty, new Binding($"{nameof(SearchLyricsViewModel.Selected)}.{nameof(CandidateItem.DisplayTitle)}"));
        sheetTitle.MaxLines = 1;

        var sheetBadge = BuildBadge();
        sheetBadge.SetBinding(Label.TextProperty, new Binding($"{nameof(SearchLyricsViewModel.Selected)}.{nameof(CandidateItem.Badge)}"));

        var preview = NewLabel(14, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: false);
        preview.LineBreakMode = LineBreakMode.NoWrap;
        preview.VerticalOptions = LayoutOptions.Start;
        preview.SetBinding(Label.TextProperty, new Binding($"{nameof(SearchLyricsViewModel.Selected)}.{nameof(CandidateItem.PreviewLyrics)}"));
        preview.SetBinding(Label.IsVisibleProperty, new Binding
        {
            Path = $"{nameof(SearchLyricsViewModel.Selected)}.{nameof(CandidateItem.PreviewLyrics)}",
            Source = _vm,
            Converter = new StringNotEmptyConverter(),
        });

        var scroll = new ScrollView
        {
            MaximumHeightRequest = 320,
            Content = preview,
        };

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
        applyButton.SetBinding(Button.CommandProperty, nameof(SearchLyricsViewModel.WriteLyricsCommand));

        var header = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        header.Add(sheetTitle, 0);
        header.Add(sheetBadge, 1);

        var panel = new Border
        {
            StrokeThickness = 0,
            Background = GetBrush("CardBackgroundStrongColor", "#2DFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(0, 0, 0, 0) },
            Padding = new Thickness(20, 16, 20, 24),
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children = { header, scroll, BuildProcessOptions(), applyButton },
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
        sheet.SetBinding(Grid.IsVisibleProperty, nameof(SearchLyricsViewModel.ShowPreview));

        return sheet;
    }

    // ── 写入前处理选项（简繁转换 / 时间偏移 / 去空行）──
    private View BuildProcessOptions()
    {
        var convPicker = new Picker
        {
            Title = "简繁转换",
            FontSize = 13,
            ItemsSource = new[] { "不转换", "繁体 → 简体", "简体 → 繁体" },
            SelectedIndex = _vm.ConversionModeIndex,
        };
        convPicker.SetBinding(Picker.SelectedIndexProperty, nameof(SearchLyricsViewModel.ConversionModeIndex));

        var convRow = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
        };
        var convLabel = NewLabel(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: true);
        convLabel.Text = "简体繁体";
        convLabel.VerticalOptions = LayoutOptions.Center;
        convRow.Add(convLabel, 0);
        convRow.Add(convPicker, 1);

        // 时间偏移：-5s ~ +5s，步进 1s
        var offsetDisplay = NewLabel(13, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", tail: true);
        offsetDisplay.VerticalOptions = LayoutOptions.Center;
        offsetDisplay.MinimumWidthRequest = 60;
        OffsetLabel = offsetDisplay;
        RefreshOffsetLabel();

        var minusBtn = MakeStepButton("−", -1, () => RefreshOffsetLabel());
        var plusBtn = MakeStepButton("＋", +1, () => RefreshOffsetLabel());

        var offsetGrid = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        var offsetRowLabel = NewLabel(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: true);
        offsetRowLabel.Text = "时间偏移";
        offsetRowLabel.VerticalOptions = LayoutOptions.Center;
        offsetGrid.Add(offsetRowLabel, 0);
        offsetGrid.Add(offsetDisplay, 1);
        offsetGrid.Add(minusBtn, 2);
        offsetGrid.Add(plusBtn, 3);

        // 去空行开关
        var emptySwitch = new Switch { IsToggled = true };
        emptySwitch.SetBinding(Switch.IsToggledProperty, nameof(SearchLyricsViewModel.RemoveEmptyLines));
        var emptyRow = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        var emptyRowLabel = NewLabel(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: true);
        emptyRowLabel.Text = "去除空行";
        emptyRowLabel.VerticalOptions = LayoutOptions.Center;
        emptyRow.Add(emptyRowLabel, 0);
        emptyRow.Add(emptySwitch, 1);

        return new Border
        {
            Margin = new Thickness(0, 2),
            StrokeThickness = 0,
            Background = GetBrush("CardBackgroundColor", "#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            Padding = new Thickness(12, 8),
            Content = new VerticalStackLayout { Spacing = 10, Children = { convRow, offsetGrid, emptyRow } },
        };
    }

    private Label? OffsetLabel;

    private void RefreshOffsetLabel()
    {
        if (OffsetLabel == null) return;
        OffsetLabel.Text = _vm.OffsetSeconds switch
        {
            > 0 => $"+{_vm.OffsetSeconds}s",
            < 0 => $"{_vm.OffsetSeconds}s",
            _ => "0s",
        };
    }

    private Button MakeStepButton(string text, int delta, Action refresh)
    {
        var btn = new Button
        {
            Text = text,
            FontSize = 16,
            TextColor = Colors.White,
            BackgroundColor = Text("PrimaryColor", "#8C7BFF"),
            CornerRadius = 8,
            WidthRequest = 34,
            HeightRequest = 30,
        };
        btn.Clicked += (_, _) =>
        {
            _vm.OffsetSeconds = (int)Math.Clamp(_vm.OffsetSeconds + delta, -5, 5);
            refresh();
        };
        return btn;
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

    private static Label BuildBadge()
    {
        var label = new Label
        {
            FontSize = 10,
            FontAttributes = FontAttributes.Bold,
            MinimumWidthRequest = 0,
        };
        label.SetDynamicResource(Label.TextColorProperty, "PrimaryColor");
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

/// <summary>歌名 + 艺人 → 折叠栏显示的关键字文本（如「Closer To Me - Megan & Liz」）。</summary>
internal sealed class CollapsedKeywordConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var title = values.Length > 0 ? values[0]?.ToString()?.Trim() : "";
        var artist = values.Length > 1 ? values[1]?.ToString()?.Trim() : "";
        if (string.IsNullOrEmpty(title)) return "搜索歌词";
        if (string.IsNullOrEmpty(artist)) return title;
        return $"{title} - {artist}";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
