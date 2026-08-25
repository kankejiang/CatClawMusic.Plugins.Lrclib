using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 「歌词匹配」入口页（纯 C# 代码构建 UI，避免跨程序集 XAML 编译问题；
/// 通过 DynamicResource 复用宿主全局主题资源，缺失时回退默认色）。
/// <para>
/// 视觉参考 Lyrico 歌词搜索页：深色背景 + 顶部圆角搜索面板 + 药丸标签（候选/已保存）
/// + 全宽卡片列表（封面色块/加粗标题/歌词形态徽标/艺人·专辑/时长）+ 点卡片弹出底部歌词预览面板。
/// </para>
/// <para>
/// 上半区：歌名/艺人搜索 → LRCLIB 候选卡片列表，点卡片弹出底部面板预览并「使用此歌词」保存为覆盖记录；
/// 「已保存」标签页：覆盖记录列表，可删除恢复自动匹配。
/// </para>
/// </summary>
public class ManualMatchPage : ContentPage
{
    private readonly ManualMatchViewModel _vm;

    public ManualMatchPage(ManualMatchViewModel vm)
    {
        _vm = vm;
        BindingContext = _vm;

        Title = "歌词匹配";
        BackgroundColor = GetResourceColor("WindowBackgroundColor", "#1A1838");

        var searchPanel = BuildSearchPanel();
        var tabRow = BuildTabRow();
        var candidatesList = BuildCandidatesList();
        var overridesList = BuildOverridesList();

        var content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
        };
        content.Add(searchPanel, 0, 0);
        content.Add(tabRow, 0, 1);
        content.Add(candidatesList, 0, 2);
        content.Add(overridesList, 0, 2);

        // 底部歌词预览面板（覆盖全页）
        var sheet = BuildCandidateSheet();
        content.Add(sheet);

        Content = content;
    }

    // ── 顶部搜索面板 ──
    private View BuildSearchPanel()
    {
        var titleEntry = new Entry { Placeholder = "歌名（需与歌曲标签一致）", TextColor = Text("TextPrimaryColor", "#F7F8FF"), PlaceholderColor = Text("TextSecondaryColor", "#C2C6E4"), BackgroundColor = Colors.Transparent };
        titleEntry.SetBinding(Entry.TextProperty, nameof(ManualMatchViewModel.SearchTitle));

        var artistEntry = new Entry { Placeholder = "艺人（可空）", TextColor = Text("TextPrimaryColor", "#F7F8FF"), PlaceholderColor = Text("TextSecondaryColor", "#C2C6E4"), BackgroundColor = Colors.Transparent };
        artistEntry.SetBinding(Entry.TextProperty, nameof(ManualMatchViewModel.SearchArtist));

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
        searchButton.SetBinding(Button.CommandProperty, nameof(ManualMatchViewModel.SearchCommand));
        searchButton.SetBinding(Button.IsEnabledProperty,
            new Binding(nameof(ManualMatchViewModel.IsBusy)) { Converter = new InverseBooleanConverter(), Source = _vm });

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

        var statusLabel = new Label
        {
            FontSize = 12,
            LineBreakMode = LineBreakMode.WordWrap,
            Margin = new Thickness(2, 6, 2, 0),
        };
        statusLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
        statusLabel.SetBinding(Label.TextProperty, nameof(ManualMatchViewModel.StatusText));

        var panel = new Border
        {
            Margin = new Thickness(12, 10, 12, 4),
            StrokeThickness = 0,
            Background = GetBrush("CardBackgroundColor", "#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Padding = new Thickness(12, 10),
            Content = new VerticalStackLayout { Spacing = 0, Children = { searchRow, statusLabel } },
        };
        return panel;
    }

    // ── 药丸标签：候选 / 已保存 ──
    private HorizontalStackLayout BuildTabRow()
    {
        var candidatePill = BuildPillButton("候选", 0);
        var savedPill = BuildPillButton("已保存", 1);

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ManualMatchViewModel.ActiveTab))
            {
                candidatePill.BackgroundColor = _vm.ActiveTab == 0 ? Text("PrimaryColor", "#8C7BFF") : Text("CardBackgroundColor", "#1AFFFFFF");
                candidatePill.TextColor = _vm.ActiveTab == 0 ? Colors.White : Text("TextSecondaryColor", "#C2C6E4");
                savedPill.BackgroundColor = _vm.ActiveTab == 1 ? Text("PrimaryColor", "#8C7BFF") : Text("CardBackgroundColor", "#1AFFFFFF");
                savedPill.TextColor = _vm.ActiveTab == 1 ? Colors.White : Text("TextSecondaryColor", "#C2C6E4");
            }
        };

        return new HorizontalStackLayout
        {
            Spacing = 8,
            Margin = new Thickness(12, 4, 12, 6),
            Children = { candidatePill, savedPill },
        };
    }

    private Button BuildPillButton(string text, int index)
    {
        var btn = new Button
        {
            Text = text,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = index == 0 ? Colors.White : Text("TextSecondaryColor", "#C2C6E4"),
            BackgroundColor = index == 0 ? Text("PrimaryColor", "#8C7BFF") : Text("CardBackgroundColor", "#1AFFFFFF"),
            CornerRadius = 16,
            Padding = new Thickness(14, 2),
            HeightRequest = 32,
        };
        btn.Clicked += (_, _) => _vm.ActiveTab = index;
        return btn;
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
        list.SetBinding(CollectionView.ItemsSourceProperty, nameof(ManualMatchViewModel.Candidates));
        BindTabVisibility(list, 0);

        return list;
    }

    private View BuildOverridesList()
    {
        var list = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            Margin = new Thickness(0),
            ItemTemplate = new DataTemplate(CreateOverrideCard),
        };
        list.SetBinding(CollectionView.ItemsSourceProperty, nameof(ManualMatchViewModel.Overrides));
        BindTabVisibility(list, 1);

        return list;
    }

    private void BindTabVisibility(View view, int tab)
    {
        view.SetBinding(IsVisibleProperty, new Binding
        {
            Source = _vm,
            Path = nameof(ManualMatchViewModel.ActiveTab),
            Converter = new TabVisibleConverter(tab),
        });
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
        badge.SetBinding(Label.TextColorProperty, new Binding(nameof(CandidateItem.HasLyrics)) { Converter = new BoolToColorConverter(Text("PrimaryColor", "#8C7BFF"), Text("TextSecondaryColor", "#C2C6E4")) });

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

        // 艺人 · 专辑
        var subtitle = NewLabel(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: true);
        subtitle.SetBinding(Label.TextProperty, nameof(CandidateItem.Subtitle));
        subtitle.MaxLines = 1;

        var textStack = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center, Children = { titleRow, subtitle } };

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
        card.SetBinding(OpacityProperty, new Binding(nameof(CandidateItem.HasLyrics)) { Converter = new BoolToValueConverter(1.0, 0.45) });

        var tap = new TapGestureRecognizer();
        tap.SetBinding(TapGestureRecognizer.CommandParameterProperty, new Binding("."));
        card.GestureRecognizers.Add(tap);
        tap.SetBinding(TapGestureRecognizer.CommandProperty, new Binding(
            nameof(ManualMatchViewModel.OpenCandidateCommand), source: _vm));

        return card;
    }

    // ── 覆盖记录卡片 ──
    private View CreateOverrideCard()
    {
        var title = NewLabel(15, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", tail: true);
        title.SetBinding(Label.TextProperty, nameof(OverrideItem.Display));
        title.MaxLines = 1;

        var subtitle = NewLabel(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: true);
        subtitle.SetBinding(Label.TextProperty, nameof(OverrideItem.Subtitle));
        subtitle.MaxLines = 2;

        var removeButton = new Button
        {
            Text = "删除",
            FontSize = 13,
            TextColor = Text("TextSecondaryColor", "#C2C6E4"),
            BackgroundColor = Text("CardBackgroundStrongColor", "#2DFFFFFF"),
            CornerRadius = 14,
            Padding = new Thickness(12, 2),
            HeightRequest = 28,
        };
        removeButton.SetBinding(Button.CommandProperty, nameof(ManualMatchViewModel.RemoveOverrideCommand));
        removeButton.SetBinding(Button.CommandParameterProperty, new Binding("."));

        var textStack = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center, Children = { title, subtitle } };
        var row = new Grid
        {
            ColumnSpacing = 10,
            Padding = new Thickness(14, 10),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        row.Add(textStack, 0);
        row.Add(removeButton, 1);

        return new Border
        {
            Margin = new Thickness(12, 3),
            StrokeThickness = 0,
            Background = GetBrush("CardBackgroundColor", "#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Content = row,
        };
    }

    // ── 底部歌词预览面板 ──
    private Grid BuildCandidateSheet()
    {
        var scrim = new BoxView { Color = Color.FromArgb("#8C000000") };
        var scrimTap = new TapGestureRecognizer();
        scrimTap.SetBinding(TapGestureRecognizer.CommandProperty, new Binding(nameof(ManualMatchViewModel.CloseSheetCommand), source: _vm));
        scrim.GestureRecognizers.Add(scrimTap);

        var sheetTitle = NewLabel(18, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", tail: true);
        sheetTitle.SetBinding(Label.TextProperty, new Binding($"{nameof(ManualMatchViewModel.SelectedCandidate)}.{nameof(CandidateItem.DisplayTitle)}"));
        sheetTitle.MaxLines = 1;

        var sheetBadge = BuildBadge();
        sheetBadge.SetBinding(Label.TextProperty, new Binding($"{nameof(ManualMatchViewModel.SelectedCandidate)}.{nameof(CandidateItem.Badge)}"));

        var preview = new Label
        {
            FontSize = 14,
            LineBreakMode = LineBreakMode.NoWrap,
            TextColor = Text("TextSecondaryColor", "#C2C6E4"),
            VerticalOptions = LayoutOptions.Start,
        };
        preview.SetBinding(Label.TextProperty, new Binding($"{nameof(ManualMatchViewModel.SelectedCandidate)}.{nameof(CandidateItem.PreviewLyrics)}"));
        preview.SetBinding(Label.IsVisibleProperty, new Binding { Path = $"{nameof(ManualMatchViewModel.SelectedCandidate)}.{nameof(CandidateItem.PreviewLyrics)}", Source = _vm, Converter = new StringNotEmptyConverter() });

        var scroll = new ScrollView
        {
            MaximumHeightRequest = 320,
            Content = preview,
        };

        var applyButton = new Button
        {
            Text = "使用此歌词",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = Text("PrimaryColor", "#8C7BFF"),
            CornerRadius = 20,
            HeightRequest = 40,
        };
        applyButton.SetBinding(Button.CommandProperty, nameof(ManualMatchViewModel.ApplySelectedCommand));

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
                Children = { header, scroll, applyButton },
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
        sheet.SetBinding(Grid.IsVisibleProperty, nameof(ManualMatchViewModel.ShowCandidateSheet));

        return sheet;
    }

    // ── 小工具 ──
    private static Label NewLabel(double fontSize, FontAttributes weight, string key, string fallback, bool tail)
    {
        var label = new Label { FontSize = fontSize, FontAttributes = weight, LineBreakMode = tail ? LineBreakMode.TailTruncation : LineBreakMode.WordWrap };
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

/// <summary>布尔取反（忙碌时禁用搜索按钮、隐藏占位）</summary>
internal class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>字符串非空 → 可见</summary>
internal class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is string s && !string.IsNullOrWhiteSpace(s);

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>按当前标签是否等于目标标签来决定可见性</summary>
internal class TabVisibleConverter : IValueConverter
{
    private readonly int _tab;

    public TabVisibleConverter(int tab) => _tab = tab;

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is int v && v == _tab;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>布尔 → 双值输出（true/false 映射不同对象）</summary>
internal class BoolToValueConverter : IValueConverter
{
    private readonly object _true;
    private readonly object _false;

    public BoolToValueConverter(object trueValue, object falseValue) { _true = trueValue; _false = falseValue; }

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b && b ? _true : _false;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>布尔 → 歌词徽标文字颜色：有歌词用主色，无歌词用次级色</summary>
internal class BoolToColorConverter : IValueConverter
{
    private readonly object _true;
    private readonly object _false;

    public BoolToColorConverter(object trueValue, object falseValue) { _true = trueValue; _false = falseValue; }

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b && b ? _true : _false;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}