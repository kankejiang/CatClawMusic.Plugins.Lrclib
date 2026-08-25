using Microsoft.Maui.Controls;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 批量导出页（Lyrico <c>BatchExportScreen</c> 复刻）：
/// 选目标文件夹 → 勾选导出歌词(.lrc)/封面(.jpg) → 逐首读标签写文件 → 结果列表。
/// 纯 C# 构建，复用宿主全局主题资源。
/// </summary>
public class BatchExportPage : ContentPage
{
    private readonly BatchExportViewModel _vm;

    public BatchExportPage(IReadOnlyList<SongItem> songs)
    {
        _vm = new BatchExportViewModel(songs, PluginHost.AudioFiles);
        BindingContext = _vm;

        Title = "批量导出";
        BackgroundColor = GetResourceColor("WindowBackgroundColor", "#1A1838");

        // ── 文件夹路径 ──
        var folderEntry = new Entry
        {
            Placeholder = "目标文件夹路径（如 D:\\Lyrics，桌面端）",
            FontSize = 14,
        };
        folderEntry.SetDynamicResource(Entry.TextColorProperty, "TextPrimaryColor");
        folderEntry.SetDynamicResource(Entry.PlaceholderColorProperty, "TextSecondaryColor");
        folderEntry.SetBinding(Entry.TextProperty,
            new Binding(nameof(BatchExportViewModel.FolderPath), BindingMode.TwoWay));

        // ── 导出选项 ──
        var lyricsSwitch = new Switch { OnColor = GetResourceColor("PrimaryColor", "#8C7BFF") };
        lyricsSwitch.SetBinding(Switch.IsToggledProperty,
            new Binding(nameof(BatchExportViewModel.ExportLyrics), BindingMode.TwoWay));
        var lyricsLabel = MakeRowLabel("导出歌词（.lrc）");

        var coverSwitch = new Switch { OnColor = GetResourceColor("PrimaryColor", "#8C7BFF") };
        coverSwitch.SetBinding(Switch.IsToggledProperty,
            new Binding(nameof(BatchExportViewModel.ExportCover), BindingMode.TwoWay));
        var coverLabel = MakeRowLabel("导出封面（.jpg）");

        var optionsGrid = new Grid
        {
            Padding = new Thickness(0, 8),
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            RowSpacing = 12,
        };
        optionsGrid.Add(lyricsLabel, 0, 0);
        optionsGrid.Add(lyricsSwitch, 1, 0);
        optionsGrid.Add(coverLabel, 0, 1);
        optionsGrid.Add(coverSwitch, 1, 1);

        // ── 导出按钮 ──
        var exportButton = new Button
        {
            Text = "开始导出",
            FontSize = 14,
            TextColor = Colors.White,
            BackgroundColor = GetResourceColor("PrimaryColor", "#8C7BFF"),
            CornerRadius = 14,
            Padding = new Thickness(16, 10),
            Margin = new Thickness(0, 8, 0, 4),
        };
        exportButton.SetBinding(Button.CommandProperty, nameof(BatchExportViewModel.ExportCommand));
        exportButton.SetBinding(Button.IsEnabledProperty,
            new Binding(nameof(BatchExportViewModel.IsBusy)) { Converter = new InverseBooleanConverter(), Source = _vm });

        // ── 状态 ──
        var status = new Label { FontSize = 12, LineBreakMode = LineBreakMode.WordWrap };
        status.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
        status.SetBinding(Label.TextProperty, nameof(BatchExportViewModel.StatusText));

        // ── 结果列表 ──
        var resultsView = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            Margin = new Thickness(0, 8, 0, 0),
            ItemTemplate = new DataTemplate(() =>
            {
                var display = new Label { FontSize = 13, LineBreakMode = LineBreakMode.TailTruncation };
                display.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");
                display.SetBinding(Label.TextProperty, nameof(ExportResultItem.Display));
                var result = new Label { FontSize = 11 };
                result.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
                result.SetBinding(Label.TextProperty, nameof(ExportResultItem.Result));
                return new VerticalStackLayout { Spacing = 2, Padding = new Thickness(4, 4), Children = { display, result } };
            }),
        };
        resultsView.SetBinding(CollectionView.ItemsSourceProperty, nameof(BatchExportViewModel.Results));

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(16, 12),
                Spacing = 8,
                Children = { folderEntry, optionsGrid, exportButton, status, resultsView },
            }
        };
    }

    private static Label MakeRowLabel(string text)
    {
        var l = new Label { Text = text, FontSize = 14, VerticalOptions = LayoutOptions.Center };
        l.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");
        return l;
    }

    private static Color GetResourceColor(string key, string fallback)
        => Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c
            ? c : Color.FromArgb(fallback);
}
