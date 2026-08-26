using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 批量操作页（Lyrico 批量整理复刻）：
/// 多选歌曲进入本页，按模式（批量歌词 / 批量编辑）逐首处理。
/// 顶部状态 + 进度，中间按模式展示操作区，底部为逐首结果列表。
/// 纯 C# 构建，复用宿主全局主题资源，失败/无匹配的歌曲记录在结果列表不影响其余继续。
/// </summary>
public class BatchOperationsPage : ContentPage
{
    private readonly BatchOperationsViewModel _vm;

    public BatchOperationsPage(IReadOnlyList<SongItem> songs, BatchOperationMode mode)
    {
        _vm = new BatchOperationsViewModel(songs, mode, PluginHost.AudioFiles, new LrclibApiClient());
        BindingContext = _vm;

        Title = mode switch
        {
            BatchOperationMode.MatchLyrics => "批量匹配歌词",
            BatchOperationMode.EditTags => "批量编辑标签",
            BatchOperationMode.RenameFiles => "批量重命名",
            BatchOperationMode.DeleteFiles => "批量删除",
            _ => "批量操作",
        };
        BackgroundColor = GetResourceColor("WindowBackgroundColor", "#1A1838");

        var header = BuildHeader();
        var operation = mode switch
        {
            BatchOperationMode.MatchLyrics => BuildMatchLyricsArea(),
            BatchOperationMode.EditTags => BuildEditTagsArea(),
            BatchOperationMode.RenameFiles => BuildRenameArea(),
            _ => BuildDeleteArea(),
        };
        var results = BuildResultsList();

        if (mode == BatchOperationMode.RenameFiles)
            _vm.RefreshRenamePreview();

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
        root.Add(operation, 0, 1);
        root.Add(results, 0, 2);

        Content = root;
    }

    // ── 顶部：状态 + 进度 ──
    private View BuildHeader()
    {
        var status = NewLabel(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: false);
        status.LineBreakMode = LineBreakMode.WordWrap;
        status.SetBinding(Label.TextProperty, nameof(BatchOperationsViewModel.StatusText));

        var progress = NewLabel(13, FontAttributes.Bold, "PrimaryColor", "#8C7BFF", tail: true);
        progress.SetBinding(Label.TextProperty, nameof(BatchOperationsViewModel.ProgressText));

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

    // ── 批量歌词：开始匹配按钮 ──
    private View BuildMatchLyricsArea()
    {
        var hint = NewLabel(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: false);
        hint.Text = "逐首搜索 LRCLIB，有同步歌词优先写入；失败/无匹配的歌曲会记录在下方结果中。";

        var run = new Button
        {
            Text = "开始匹配",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = GetResourceColor("PrimaryColor", "#8C7BFF"),
            CornerRadius = 20,
            HeightRequest = 44,
            Margin = new Thickness(0, 10, 0, 0),
        };
        run.SetBinding(Button.CommandProperty, nameof(BatchOperationsViewModel.RunCommand));
        run.SetBinding(Button.IsEnabledProperty, new Binding(nameof(BatchOperationsViewModel.CanRun)));

        return new Border
        {
            Margin = new Thickness(12, 8, 12, 4),
            StrokeThickness = 0,
            Background = GetBrush("CardBackgroundColor", "#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Padding = new Thickness(14),
            Content = new VerticalStackLayout { Spacing = 0, Children = { hint, run } },
        };
    }

    // ── 批量重命名：格式输入 + 占位符说明 + 预览 ──
    private View BuildRenameArea()
    {
        var formatLabel = NewLabel(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: true);
        formatLabel.Text = "命名格式";
        var formatEntry = MakeEntry(nameof(BatchOperationsViewModel.RenameFormat));
        formatEntry.Placeholder = "例：@1 - @2";

        var formatRow = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };
        formatRow.Add(formatLabel, 0);
        formatRow.Add(formatEntry, 1);

        // 占位符提示（可点击填入格式尾部）
        var chipRow = new HorizontalStackLayout { Spacing = 6 };
        foreach (var hint in BatchOperationsViewModel.PlaceholderHints)
        {
            var chip = new Button
            {
                Text = hint,
                FontSize = 11,
                FontAttributes = FontAttributes.None,
                TextColor = GetResourceColor("PrimaryColor", "#8C7BFF"),
                BackgroundColor = Colors.Transparent,
                BorderColor = GetResourceColor("PrimaryColor", "#8C7BFF"),
                BorderWidth = 1,
                CornerRadius = 12,
                Padding = new Thickness(8, 1),
                HeightRequest = 26,
            };
            chip.Clicked += (_, _) =>
            {
                var code = hint.Split(' ')[0];
                formatEntry.Text += (string.IsNullOrEmpty(formatEntry.Text) ? "" : " ") + code;
            };
            chipRow.Children.Add(chip);
        }

        var preview = NewLabel(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: false);
        preview.LineBreakMode = LineBreakMode.WordWrap;
        preview.SetBinding(Label.TextProperty, nameof(BatchOperationsViewModel.RenamePreviewText));

        var run = new Button
        {
            Text = "重命名",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = GetResourceColor("PrimaryColor", "#8C7BFF"),
            CornerRadius = 20,
            HeightRequest = 44,
            Margin = new Thickness(0, 10, 0, 0),
        };
        run.SetBinding(Button.CommandProperty, nameof(BatchOperationsViewModel.RunCommand));
        run.SetBinding(Button.IsEnabledProperty, new Binding(nameof(BatchOperationsViewModel.CanRun)));

        var stack = new VerticalStackLayout { Spacing = 8 };
        stack.Add(formatRow);
        stack.Add(chipRow);
        stack.Add(preview);
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

    // ── 批量删除：红色警示 + 删除按钮 ──
    private View BuildDeleteArea()
    {
        // 危险警示固定红色（NewLabel 的 DynamicResource 键机制不适用单点回退色）
        var hint = new Label
        {
            Text = "将永久删除所选音频文件，此操作不可恢复。请确认后继续。",
            FontSize = 13,
            TextColor = Color.FromArgb("#F87171"),
        };

        var run = new Button
        {
            Text = "确认删除",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = GetResourceColor("DangerColor", "#EF4444"),
            CornerRadius = 20,
            HeightRequest = 44,
            Margin = new Thickness(0, 10, 0, 0),
        };
        run.SetBinding(Button.CommandProperty, nameof(BatchOperationsViewModel.RunCommand));
        run.SetBinding(Button.IsEnabledProperty, new Binding(nameof(BatchOperationsViewModel.CanRun)));

        return new Border
        {
            Margin = new Thickness(12, 8, 12, 4),
            StrokeThickness = 0,
            Background = GetBrush("CardBackgroundColor", "#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Padding = new Thickness(14),
            Content = new VerticalStackLayout { Spacing = 0, Children = { hint, run } },
        };
    }

    // ── 批量编辑：字段输入卡 + 应用按钮 ──
    private View BuildEditTagsArea()
    {
        var fields = new (string label, string binding)[]
        {
            ("标题", nameof(BatchOperationsViewModel.EditTitle)),
            ("艺人", nameof(BatchOperationsViewModel.EditArtist)),
            ("专辑", nameof(BatchOperationsViewModel.EditAlbum)),
            ("年份", nameof(BatchOperationsViewModel.EditYear)),
            ("流派", nameof(BatchOperationsViewModel.EditGenre)),
        };

        var grid = new Grid { RowSpacing = 10 };
        for (var i = 0; i < fields.Length; i++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var i = 0; i < fields.Length; i++)
        {
            var label = NewLabel(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: true);
            label.Text = fields[i].label;
            var entry = MakeEntry(fields[i].binding);
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                },
            };
            row.Add(label, 0);
            row.Add(entry, 1);
            grid.Add(row, 0, i);
        }

        var onlyFilled = new CheckBox
        {
            IsChecked = true,
            Color = GetResourceColor("PrimaryColor", "#8C7BFF"),
            VerticalOptions = LayoutOptions.Center,
        };
        onlyFilled.SetBinding(CheckBox.IsCheckedProperty, nameof(BatchOperationsViewModel.EditOnlyFilled));
        var onlyFilledLabel = NewLabel(13, FontAttributes.None, "TextPrimaryColor", "#F7F8FF", tail: true);
        onlyFilledLabel.Text = "仅应用已填写的字段（关掉则未填字段被清空）";
        var onlyFilledRow = new Grid
        {
            ColumnSpacing = 6,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };
        onlyFilledRow.Add(onlyFilled, 0);
        onlyFilledRow.Add(onlyFilledLabel, 1);

        var run = new Button
        {
            Text = "应用编辑",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = GetResourceColor("PrimaryColor", "#8C7BFF"),
            CornerRadius = 20,
            HeightRequest = 44,
            Margin = new Thickness(0, 10, 0, 0),
        };
        run.SetBinding(Button.CommandProperty, nameof(BatchOperationsViewModel.RunCommand));
        run.SetBinding(Button.IsEnabledProperty, new Binding(nameof(BatchOperationsViewModel.CanRun)));

        var stack = new VerticalStackLayout { Spacing = 10 };
        stack.Add(grid);
        stack.Add(onlyFilledRow);
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

    // ── 结果列表 ──
    private View BuildResultsList()
    {
        var title = NewLabel(14, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", tail: false);
        title.Text = "处理结果";
        title.Margin = new Thickness(16, 10, 16, 2);
        title.SetBinding(VisualElement.IsVisibleProperty, nameof(BatchOperationsViewModel.HasResults));

        var list = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(BuildResultRow),
        };
        list.SetBinding(CollectionView.ItemsSourceProperty, nameof(BatchOperationsViewModel.Results));

        var empty = NewLabel(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", tail: false);
        empty.Text = "暂无处理结果";
        empty.HorizontalOptions = LayoutOptions.Center;
        empty.Margin = new Thickness(0, 24);
        empty.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(BatchOperationsViewModel.HasResults))
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

    // ── 结果行：标题/艺人 + 状态 ──
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

/// <summary>十六进制颜色字符串 → Color（BatchResultItem.StatusColor）</summary>
internal class HexToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var s = value as string;
        if (string.IsNullOrWhiteSpace(s)) return Colors.Gray;
        try { return Color.FromArgb(s); }
        catch { return Colors.Gray; }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
