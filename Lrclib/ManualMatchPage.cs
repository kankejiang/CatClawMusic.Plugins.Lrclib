using Microsoft.Maui.Controls;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 「歌词匹配」入口页（纯 C# 代码构建 UI，避免跨程序集 XAML 编译问题；
/// 通过 DynamicResource 复用宿主全局主题资源，缺失时回退默认色）。
/// <para>
/// 上半区：歌名/艺人搜索 → LRCLIB 候选列表，点「使用此歌词」保存为覆盖记录；
/// 下半区：已有覆盖记录列表，可删除恢复自动匹配。
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
        BackgroundColor = GetResourceColor("WindowBackgroundColor", "#0B0D20");

        // ── 搜索区 ──
        var titleEntry = new Entry
        {
            Placeholder = "歌名（需与歌曲标签一致）",
        };
        titleEntry.SetBinding(Entry.TextProperty, nameof(ManualMatchViewModel.SearchTitle));

        var artistEntry = new Entry
        {
            Placeholder = "艺人（可空）",
        };
        artistEntry.SetBinding(Entry.TextProperty, nameof(ManualMatchViewModel.SearchArtist));

        var searchButton = new Button { Text = "搜索", FontSize = 14 };
        searchButton.SetBinding(Button.CommandProperty, nameof(ManualMatchViewModel.SearchCommand));
        searchButton.SetBinding(Button.IsEnabledProperty,
            new Binding(nameof(ManualMatchViewModel.IsBusy)) { Converter = new InverseBooleanConverter(), Source = _vm });

        var searchRow = new Grid
        {
            ColumnSpacing = 8,
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

        var statusLabel = new Label { FontSize = 12, LineBreakMode = LineBreakMode.WordWrap };
        statusLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
        statusLabel.SetBinding(Label.TextProperty, nameof(ManualMatchViewModel.StatusText));

        // ── 候选列表 ──
        var candidatesHeader = new Label { Text = "LRCLIB 候选（点「使用此歌词」为上方歌名/艺人保存）", FontSize = 13 };
        candidatesHeader.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");

        var candidatesView = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            Margin = new Thickness(0, 6, 0, 0),
            ItemTemplate = new DataTemplate(CreateCandidateTemplate),
        };
        candidatesView.SetBinding(CollectionView.ItemsSourceProperty, nameof(ManualMatchViewModel.Candidates));

        var emptyCandidates = new Label
        {
            Text = "输入歌名后点搜索",
            FontSize = 13,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 12, 0, 0),
        };
        emptyCandidates.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");

        // ── 覆盖记录 ──
        var overridesHeader = new Label { Text = "手动覆盖记录（播放时优先使用，可删除恢复自动匹配）", FontSize = 13 };
        overridesHeader.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");

        var overridesView = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            Margin = new Thickness(0, 6, 0, 0),
            ItemTemplate = new DataTemplate(CreateOverrideTemplate),
        };
        overridesView.SetBinding(CollectionView.ItemsSourceProperty, nameof(ManualMatchViewModel.Overrides));

        var emptyOverrides = new Label
        {
            Text = "暂无覆盖记录",
            FontSize = 13,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 12, 0, 0),
        };
        emptyOverrides.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");

        // ── Lyrico 源插件区（LRCLIB 未命中时的多源歌词兜底）──
        var lyricoHeader = new Label
        {
            Text = "Lyrico 源插件（导入 .zip 安装 netease/qq/… 等源，LRCLIB 未命中时兜底取词）",
            FontSize = 13,
            Margin = new Thickness(0, 16, 0, 0),
        };
        lyricoHeader.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");

        var importButton = new Button { Text = "导入源插件(.zip)", FontSize = 13, Padding = new Thickness(10, 4) };
        importButton.SetBinding(Button.CommandProperty, nameof(ManualMatchViewModel.ImportLyricoSourceCommand));
        importButton.SetBinding(Button.IsEnabledProperty,
            new Binding(nameof(ManualMatchViewModel.IsBusy)) { Converter = new InverseBooleanConverter(), Source = _vm });

        var lyricoStatus = new Label { FontSize = 12, LineBreakMode = LineBreakMode.WordWrap };
        lyricoStatus.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
        lyricoStatus.SetBinding(Label.TextProperty, nameof(ManualMatchViewModel.SourceStatusText));

        var lyricoSourcesView = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            Margin = new Thickness(0, 6, 0, 0),
            ItemTemplate = new DataTemplate(CreateLyricoSourceTemplate),
        };
        lyricoSourcesView.SetBinding(CollectionView.ItemsSourceProperty, nameof(ManualMatchViewModel.LyricoSources));

        var emptyLyrico = new Label
        {
            Text = "未安装 Lyrico 源插件（导入后此处列示加载状态）",
            FontSize = 13,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 12, 0, 0),
        };
        emptyLyrico.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");

        // ── 组装 ──
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(16, 12),
                Spacing = 8,
                Children =
                {
                    searchRow,
                    statusLabel,
                    candidatesHeader,
                    candidatesView,
                    emptyCandidates,
                    overridesHeader,
                    overridesView,
                    emptyOverrides,
                    lyricoHeader,
                    importButton,
                    lyricoStatus,
                    lyricoSourcesView,
                    emptyLyrico,
                }
            }
        };

        // 候选为空时隐藏空态提示？简化处理：保持常显（CollectionView 空列表自然不渲染行）
    }

    /// <summary>候选行：标题 - 艺人 / 专辑 · 时长 + 徽标 + 「使用此歌词」按钮</summary>
    private View CreateCandidateTemplate()
    {
        var titleLabel = new Label { FontSize = 15, LineBreakMode = LineBreakMode.TailTruncation };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");
        titleLabel.SetBinding(Label.TextProperty, nameof(CandidateItem.DisplayTitle));

        var subtitleLabel = new Label { FontSize = 12, LineBreakMode = LineBreakMode.TailTruncation };
        subtitleLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
        subtitleLabel.SetBinding(Label.TextProperty, nameof(CandidateItem.Subtitle));

        var badgeLabel = new Label { FontSize = 11 };
        badgeLabel.SetBinding(Label.TextProperty, nameof(CandidateItem.Badge));
        badgeLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");

        var saveButton = new Button { Text = "使用此歌词", FontSize = 13, Padding = new Thickness(10, 4) };
        saveButton.SetBinding(Button.CommandProperty,
            new Binding(nameof(ManualMatchViewModel.SaveOverrideCommand), source: _vm));
        saveButton.SetBinding(Button.CommandParameterProperty, new Binding("."));
        saveButton.SetBinding(Button.IsEnabledProperty, nameof(CandidateItem.CanSave));

        var textStack = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { titleLabel, new HorizontalStackLayout { Spacing = 8, Children = { subtitleLabel, badgeLabel } } },
        };

        var row = new Grid
        {
            Padding = new Thickness(4, 6),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        row.Add(textStack, 0);
        row.Add(saveButton, 1);

        return row;
    }

    /// <summary>覆盖记录行：歌曲 / 选定的曲目 + 「删除」按钮</summary>
    private View CreateOverrideTemplate()
    {
        var titleLabel = new Label { FontSize = 15, LineBreakMode = LineBreakMode.TailTruncation };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");
        titleLabel.SetBinding(Label.TextProperty, nameof(OverrideItem.Display));

        var subtitleLabel = new Label { FontSize = 12, LineBreakMode = LineBreakMode.TailTruncation };
        subtitleLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
        subtitleLabel.SetBinding(Label.TextProperty, nameof(OverrideItem.Subtitle));

        var removeButton = new Button { Text = "删除", FontSize = 13, Padding = new Thickness(10, 4) };
        removeButton.SetBinding(Button.CommandProperty,
            new Binding(nameof(ManualMatchViewModel.RemoveOverrideCommand), source: _vm));
        removeButton.SetBinding(Button.CommandParameterProperty, new Binding("."));

        var textStack = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { titleLabel, subtitleLabel },
        };

        var row = new Grid
        {
            Padding = new Thickness(4, 6),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        row.Add(textStack, 0);
        row.Add(removeButton, 1);

        return row;
    }

    /// <summary>Lyrico 源插件行：名称（带状态） + 「配置」「卸载」按钮</summary>
    private View CreateLyricoSourceTemplate()
    {
        var nameLabel = new Label { FontSize = 15, LineBreakMode = LineBreakMode.TailTruncation };
        nameLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");
        nameLabel.SetBinding(Label.TextProperty, nameof(LyricoSourceItem.NameWithStatus));

        var dirLabel = new Label { FontSize = 11, LineBreakMode = LineBreakMode.TailTruncation };
        dirLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
        dirLabel.SetBinding(Label.TextProperty, nameof(LyricoSourceItem.Dir));

        var configButton = new Button { Text = "配置", FontSize = 13, Padding = new Thickness(10, 4) };
        configButton.SetBinding(Button.CommandProperty,
            new Binding(nameof(ManualMatchViewModel.OpenSourceConfigCommand), source: _vm));
        configButton.SetBinding(Button.CommandParameterProperty, new Binding("."));
        configButton.SetBinding(Button.IsVisibleProperty, nameof(LyricoSourceItem.HasConfig));

        var toggleButton = new Button { FontSize = 13, Padding = new Thickness(10, 4) };
        toggleButton.SetBinding(Button.CommandProperty,
            new Binding(nameof(ManualMatchViewModel.ToggleSourceEnabledCommand), source: _vm));
        toggleButton.SetBinding(Button.CommandParameterProperty, new Binding("."));
        toggleButton.SetBinding(Button.TextProperty, nameof(LyricoSourceItem.ToggleText));

        var testButton = new Button { Text = "测试", FontSize = 13, Padding = new Thickness(10, 4) };
        testButton.SetBinding(Button.CommandProperty,
            new Binding(nameof(ManualMatchViewModel.OpenSourceTestCommand), source: _vm));
        testButton.SetBinding(Button.CommandParameterProperty, new Binding("."));

        var deleteButton = new Button { Text = "卸载", FontSize = 13, Padding = new Thickness(10, 4) };
        deleteButton.SetBinding(Button.CommandProperty,
            new Binding(nameof(ManualMatchViewModel.DeleteLyricoSourceCommand), source: _vm));
        deleteButton.SetBinding(Button.CommandParameterProperty, new Binding("."));

        var textStack = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { nameLabel, dirLabel },
        };

        var buttons = new HorizontalStackLayout { Spacing = 6, Children = { configButton, toggleButton, testButton, deleteButton } };

        var row = new Grid
        {
            Padding = new Thickness(4, 6),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        row.Add(textStack, 0);
        row.Add(buttons, 1);

        return row;
    }

    private static Color GetResourceColor(string key, string fallback)
        => Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c
            ? c
            : Color.FromArgb(fallback);
}

/// <summary>布尔取反（忙碌时禁用搜索按钮）</summary>
internal class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
