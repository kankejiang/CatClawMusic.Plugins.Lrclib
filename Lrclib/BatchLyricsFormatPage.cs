using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 批量歌词格式页（Lyrico <c>BatchLyricsFormatScreen</c> 复刻）：
/// 多选歌曲进入，选择目标格式（普通 LRC / 增强 LRC / TTML），可选去空行、过滤标签行，
/// 逐首读取内嵌歌词 → 转换 → 写回，结果记录在下方列表。
/// 纯 C# 构建，复用宿主全局主题资源。
/// </summary>
public class BatchLyricsFormatPage : ContentPage
{
    private readonly BatchLyricsFormatViewModel _vm;

    public BatchLyricsFormatPage(IReadOnlyList<SongItem> songs)
    {
        _vm = new BatchLyricsFormatViewModel(songs, PluginHost.AudioFiles);
        BindingContext = _vm;

        Title = "批量歌词格式";
        BackgroundColor = GetResourceColor("WindowBackgroundColor", "#1A1838");

        var header = BuildHeader();
        var config = BuildConfigArea();
        var results = BuildResultsList();

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
        };
        root.Add(header, 0, 0);
        root.Add(config, 0, 1);
        root.Add(results, 0, 2);

        Content = root;
        WideAdapt.Attach(this);
    }

    // ── 顶部：状态 + 进度 ──
    private View BuildHeader()
    {
        var status = NewLabel(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: false);
        status.LineBreakMode = LineBreakMode.WordWrap;
        status.SetBinding(Label.TextProperty, nameof(BatchLyricsFormatViewModel.StatusText));

        var progress = NewLabel(13, FontAttributes.Bold, "PrimaryColor", "#8C7BFF", tail: true);
        progress.SetBinding(Label.TextProperty, nameof(BatchLyricsFormatViewModel.ProgressText));

        return new Border
        {
            Margin = new Thickness(12, 10, 12, 4),
            StrokeThickness = 0,
            Background = GetBrush("CardBackgroundColor", "#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Padding = new Thickness(14, 10),
            Content = new VerticalStackLayout { Spacing = 6, Children = { status, progress } },
        };
    }

    // ── 配置区：目标格式 + 选项 + 开始按钮 ──
    private View BuildConfigArea()
    {
        var fmtLabel = NewLabel(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: true);
        fmtLabel.Text = "目标格式";
        fmtLabel.VerticalOptions = LayoutOptions.Center;

        var picker = new Picker
        {
            FontSize = 13,
            Title = "选择目标格式",
            ItemsSource = _vm.TargetFormatOptions,
            SelectedIndex = _vm.TargetFormatIndex,
        };
        picker.SetBinding(Picker.SelectedIndexProperty, nameof(BatchLyricsFormatViewModel.TargetFormatIndex));

        var fmtRow = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };
        fmtRow.Add(fmtLabel, 0);
        fmtRow.Add(picker, 1);

        // 去空行
        var emptySwitch = new Switch { IsToggled = false, VerticalOptions = LayoutOptions.Center };
        emptySwitch.SetBinding(Switch.IsToggledProperty, nameof(BatchLyricsFormatViewModel.RemoveEmptyLines));
        var emptyLabel = NewLabel(13, FontAttributes.None, "TextPrimaryColor", "#F7F8FF", tail: true);
        emptyLabel.Text = "去除空行 / 占位行";
        var emptyRow = MakeOptionRow(emptyLabel, emptySwitch);

        // 过滤标签行（TagLine）
        var tagSwitch = new Switch { IsToggled = false, VerticalOptions = LayoutOptions.Center };
        tagSwitch.SetBinding(Switch.IsToggledProperty, nameof(BatchLyricsFormatViewModel.RemoveTagLines));
        var tagLabel = NewLabel(13, FontAttributes.None, "TextPrimaryColor", "#F7F8FF", tail: true);
        tagLabel.Text = "过滤含标签关键词的行";
        var tagRow = MakeOptionRow(tagLabel, tagSwitch);

        // 标签关键词输入（仅在过滤开关打开时使用）
        var kwEntry = MakeEntry(nameof(BatchLyricsFormatViewModel.TagKeywords));
        kwEntry.Placeholder = "[ar: [al: [offset: [by: [re: [ve:";
        var kwRow = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };
        var kwLabel = NewLabel(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: true);
        kwLabel.Text = "关键词";
        kwLabel.VerticalOptions = LayoutOptions.Center;
        kwRow.Add(kwLabel, 0);
        kwRow.Add(kwEntry, 1);

        var hint = NewLabel(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: false);
        hint.LineBreakMode = LineBreakMode.WordWrap;
        hint.Text = "逐首读取内嵌歌词并转换为目标格式；增强 LRC 需要源歌词含词级时间轴，否则自动退化为普通行。";

        var run = new Button
        {
            Text = "开始转换",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = GetResourceColor("PrimaryColor", "#8C7BFF"),
            CornerRadius = 20,
            HeightRequest = 44,
            Margin = new Thickness(0, 10, 0, 0),
        };
        run.SetBinding(Button.CommandProperty, nameof(BatchLyricsFormatViewModel.RunCommand));
        run.SetBinding(Button.IsEnabledProperty, new Binding(nameof(BatchLyricsFormatViewModel.CanRun)));

        var stack = new VerticalStackLayout { Spacing = 10 };
        stack.Add(fmtRow);
        stack.Add(emptyRow);
        stack.Add(tagRow);
        stack.Add(kwRow);
        stack.Add(hint);
        stack.Add(run);

        return new Border
        {
            Margin = new Thickness(12, 8, 12, 4),
            StrokeThickness = 0,
            Background = GetBrush("CardBackgroundColor", "#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Padding = new Thickness(14),
            Content = stack,
        };
    }

    private static View MakeOptionRow(Label label, View control)
    {
        var row = new Grid
        {
            ColumnSpacing = 6,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        row.Add(label, 0);
        row.Add(control, 1);
        return row;
    }

    // ── 结果列表 ──
    private View BuildResultsList()
    {
        var title = NewLabel(14, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", tail: false);
        title.Text = "处理结果";
        title.Margin = new Thickness(16, 10, 16, 2);
        title.SetBinding(VisualElement.IsVisibleProperty, nameof(BatchLyricsFormatViewModel.HasResults));

        var list = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(BuildResultRow),
        };
        list.SetBinding(CollectionView.ItemsSourceProperty, nameof(BatchLyricsFormatViewModel.Results));

        var empty = NewLabel(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: false);
        empty.Text = "暂无处理结果";
        empty.HorizontalOptions = LayoutOptions.Center;
        empty.Margin = new Thickness(0, 24);
        empty.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(BatchLyricsFormatViewModel.HasResults))
        {
            Converter = new InverseBooleanConverter(),
        });

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
        };
        root.Add(title, 0, 0);
        root.Add(list, 0, 1);
        root.Add(empty, 0, 1);
        return root;
    }

    private View BuildResultRow()
    {
        var title = NewLabel(14, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", tail: true);
        title.SetBinding(Label.TextProperty, nameof(BatchResultItem.Title));
        title.MaxLines = 1;
        var subtitle = NewLabel(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: true);
        subtitle.SetBinding(Label.TextProperty, nameof(BatchResultItem.Subtitle));
        subtitle.MaxLines = 1;

        var textStack = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { title, subtitle },
        };

        var status = NewLabel(12, FontAttributes.Bold, "TextSecondaryColor", "#C2C6E4", tail: true);
        status.SetBinding(Label.TextProperty, nameof(BatchResultItem.Status));
        status.SetBinding(Label.TextColorProperty, new Binding(nameof(BatchResultItem.StatusColor))
        {
            Converter = new HexToColorConverter(),
        });

        var row = new Grid
        {
            ColumnSpacing = 10,
            Padding = new Thickness(16, 8),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        row.Add(textStack, 0);
        row.Add(status.CenteredY(), 1);
        return row;
    }

    // ── 小工具 ──
    private static Entry MakeEntry(string binding)
    {
        var e = new Entry
        {
            TextColor = GetResourceColor("TextPrimaryColor", "#F7F8FF"),
            PlaceholderColor = GetResourceColor("TextSecondaryColor", "#C2C6E4"),
            BackgroundColor = Colors.Transparent,
        };
        e.SetBinding(Entry.TextProperty, binding);
        return e;
    }

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

    private static Brush GetBrush(string key, string fallback)
        => new SolidColorBrush(GetResourceColor(key, fallback));

    private static Color GetResourceColor(string key, string fallback)
        => Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c
            ? c
            : Color.FromArgb(fallback);
}