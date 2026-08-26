using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 统一搜索页：同时搜索歌词（LRCLIB + Lyrico 多源）和封面（iTunes），
/// 结果合并展示（封面缩略图 + 标题/艺人 + 歌词/封面标记），
/// 点击结果弹出底部抽屉（封面 + 歌词预览），一键写入标签。
/// 抽屉直接作为页面根 Grid 的子元素（复刻宿主 AppBottomSheet 思路），避免 ContentView 包装在嵌入式宿主中高度计算不可靠。
/// </summary>
public class UnifiedSearchPage : ContentPage
{
    private readonly UnifiedSearchViewModel _vm;
    private bool _searchExpanded;
    private View? _searchBarRow;
    private View? _searchPanel;
    private VerticalStackLayout? _searchContainer;

    // 底部抽屉元素
    private BoxView? _sheetMask;
    private Border? _sheetCard;
    private bool _sheetOpen;
    private double _panStartY;
    private double _panStartTy;

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

        // 内容区：搜索栏 + 列表（上下布局）
        var contentGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
        };
        contentGrid.Add(_searchContainer, 0, 0);
        contentGrid.Add(list, 0, 1);

        // 根布局：单一行 Grid（Star = 占满全屏），内容和抽屉都在这一行里
        // 抽屉用 VerticalOptions=End 贴底，彻底避开 RowSpan 坑
        var root = new Grid();
        root.Children.Add(contentGrid);

        // 底部抽屉：遮罩 + 卡片，叠在最上层
        BuildSheet(root);
        _vm.Applied += async (_, _) => await CloseSheetAsync();

        Content = root;
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
            OpenSheet();
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

    // ── 底部抽屉（遮罩 + 卡片，根 Grid 只有一行 Star，VerticalOptions=End 可靠贴底）──
    private void BuildSheet(Grid rootGrid)
    {
        _sheetMask = new BoxView
        {
            Color = Color.FromArgb("#66000000"),
            Opacity = 0,
            IsVisible = false,
            InputTransparent = false,
        };
        var maskTap = new TapGestureRecognizer();
        maskTap.Tapped += async (_, _) => await CloseSheetAsync();
        _sheetMask.GestureRecognizers.Add(maskTap);

        // 抓握条
        var gripBar = new BoxView
        {
            WidthRequest = 36,
            HeightRequest = 4,
            CornerRadius = 2,
            Color = Color.FromArgb("#50000000"),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        var grip = new Grid
        {
            HeightRequest = 32,
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Start,
            Children = { gripBar },
        };
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnGripPan;
        grip.GestureRecognizers.Add(pan);

        var previewContent = BuildPreviewContent();

        var sheetGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
        };
        sheetGrid.Add(grip, 0, 0);
        sheetGrid.Add(previewContent, 0, 1);

        _sheetCard = new Border
        {
            Background = ThemeHelper.Brush("CardBackgroundStrongColor", "#FF2A254E"),
            Stroke = ThemeHelper.Brush("GlassStrokeStrongColor", "#33FFFFFF"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(22, 22, 0, 0) },
            Padding = new Thickness(16, 10, 16, 16),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(8, 0, 8, 8),
            TranslationY = 600,
            Opacity = 0,
            IsVisible = false,
            InputTransparent = false,
            Content = sheetGrid,
        };

        // 遮罩和卡片都在根 Grid 的同一 Star 行里
        // 遮罩 Fill 占满，卡片 End 贴底
        rootGrid.Children.Add(_sheetMask);
        rootGrid.Children.Add(_sheetCard);
    }

    private View BuildPreviewContent()
    {
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

        return new VerticalStackLayout
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
        };
    }

    private void OpenSheet()
    {
        if (_sheetOpen || _sheetMask == null || _sheetCard == null) return;
        _sheetOpen = true;
        _sheetMask.Opacity = 0;
        _sheetCard.Opacity = 0;
        _sheetCard.TranslationY = 600;
        _sheetMask.IsVisible = true;
        _sheetCard.IsVisible = true;
        var screenH = ResolveScreenHeight();
        var sheetH = screenH * 0.8;
        _sheetCard.HeightRequest = sheetH;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            _ = _sheetMask.FadeTo(1, 200, Easing.CubicOut);
            await AnimateSheetCardTranslationYAsync(600, 0, 300);
            SetSheetCardTranslationY(0);
        });
    }

    private async Task CloseSheetAsync()
    {
        if (!_sheetOpen || _sheetMask == null || _sheetCard == null) return;
        _sheetOpen = false;
        try
        {
            _ = _sheetMask.FadeTo(0, 180, Easing.CubicIn);
            await AnimateSheetCardTranslationYAsync(_sheetCard.TranslationY, 600, 200);
            await _sheetCard.FadeTo(0, 180, Easing.CubicIn);
            _sheetMask.IsVisible = false;
            _sheetCard.IsVisible = false;
        }
        catch { }
    }

    private void SetSheetCardTranslationY(double dp)
    {
        if (_sheetCard == null) return;
        _sheetCard.TranslationY = dp;
#if ANDROID
        try
        {
            if (_sheetCard.Handler?.PlatformView is global::Android.Views.View nv)
                nv.TranslationY = (float)(dp * nv.Resources!.DisplayMetrics!.Density);
        }
        catch { }
#endif
    }

    private async Task AnimateSheetCardTranslationYAsync(double from, double to, uint durationMs)
    {
        const int frameMs = 16;
        for (var t = 0; t < durationMs; t += frameMs)
        {
            await Task.Delay(frameMs);
            var p = Math.Min(1.0, (t + frameMs) / (double)durationMs);
            var eased = 1 - Math.Pow(1 - p, 3);
            SetSheetCardTranslationY(from + (to - from) * eased);
        }
        SetSheetCardTranslationY(to);
    }

    private double ResolveScreenHeight()
    {
#if ANDROID
        try
        {
            var act = Platform.CurrentActivity;
            var bounds = act?.Window?.WindowManager?.CurrentWindowMetrics?.Bounds;
            if (bounds is { } b && b.Height() > 0)
            {
                var d = act!.Resources!.DisplayMetrics!.Density;
                if (d > 0) return b.Height() / d;
            }
        }
        catch { }
#endif
        if (Height > 0) return Height;
        try
        {
            var d = DeviceDisplay.Current.MainDisplayInfo;
            var h = d.Height / d.Density;
            if (h > 0) return h;
        }
        catch { }
        return 800;
    }

    private void OnGripPan(object? sender, PanUpdatedEventArgs e)
    {
        if (_sheetCard == null) return;
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStartY = e.TotalY;
                _panStartTy = _sheetCard.TranslationY;
                break;
            case GestureStatus.Running:
                var dy = e.TotalY - _panStartY;
                if (dy > 0) SetSheetCardTranslationY(_panStartTy + dy);
                break;
            case GestureStatus.Completed:
                if (_sheetCard.TranslationY > ResolveScreenHeight() * 0.25)
                    _ = CloseSheetAsync();
                else
                    _ = AnimateSheetCardTranslationYAsync(_sheetCard.TranslationY, 0, 180);
                break;
        }
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
