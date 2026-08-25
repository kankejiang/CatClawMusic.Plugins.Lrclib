using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using IOPath = System.IO.Path;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 批量标签导出/导入页（复刻 Lyrico BatchExport 场景）：
/// 导出所选歌曲的标准标签/歌词为 JSON；或从之前导出的文件按路径/标题匹配导入写回。
/// </summary>
public class BatchTagTransferPage : ContentPage
{
    private readonly BatchTagTransferViewModel _vm;

    public BatchTagTransferPage(IReadOnlyList<SongItem> songs)
    {
        _vm = new BatchTagTransferViewModel(songs, PluginHost.AudioFiles);
        BindingContext = _vm;

        Title = "批量标签";
        BackgroundColor = ThemeHelper.Color("WindowBackgroundColor", "#1A1838");
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BatchTagTransferViewModel.ModeIndex))
                UpdateFilePickerVisibility();
        };

        Content = BuildContent();
    }

    private View BuildContent()
    {
        var root = new VerticalStackLayout
        {
            Spacing = 10,
            Padding = new Thickness(12, 8),
            Children =
            {
                BuildConfigArea(),
                BuildResultsArea(),
            },
        };
        return root;
    }

    private View BuildConfigArea()
    {
        var picker = new Picker
        {
            FontSize = 14,
            ItemsSource = _vm.ModeOptions,
            SelectedIndex = _vm.ModeIndex,
        };
        picker.SetBinding(Picker.SelectedIndexProperty, nameof(BatchTagTransferViewModel.ModeIndex));

        var files = _vm.GetExportFiles();
        _filePicker = new Picker
        {
            FontSize = 13,
            ItemsSource = files.Select(IOPath.GetFileName).ToList(),
            Title = "选择要导入的导出文件",
        };
        if (files.Count > 0) _filePicker.SelectedIndex = 0;
        _filePicker.SelectedIndexChanged += (_, _) =>
        {
            if (_filePicker.SelectedIndex >= 0 && _filePicker.SelectedIndex < files.Count)
                _vm.SelectedExportFile = files[_filePicker.SelectedIndex];
        };
        if (files.Count > 0) _vm.SelectedExportFile = files[0];
        _filePicker.IsVisible = _vm.ModeIndex == 1;

        var run = new Button
        {
            Text = "开始",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = ThemeHelper.Color("PrimaryColor", "#8C7BFF"),
            CornerRadius = 20,
            HeightRequest = 44,
            Margin = new Thickness(0, 8, 0, 0),
        };
        run.SetBinding(Button.CommandProperty, nameof(BatchTagTransferViewModel.RunCommand));

        return new Border
        {
            Margin = new Thickness(12, 8, 12, 4),
            StrokeThickness = 0,
            Background = ThemeHelper.Brush("CardBackgroundColor", "#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Padding = new Thickness(14),
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children = { picker, _filePicker, MakeStatusLabel(), run },
            },
        };
    }

    private Picker? _filePicker;

    private void UpdateFilePickerVisibility()
    {
        if (_filePicker != null) _filePicker.IsVisible = _vm.ModeIndex == 1;
    }

    private View MakeStatusLabel()
    {
        var status = ThemeHelper.Label(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", false);
        status.SetBinding(Label.TextProperty, nameof(BatchTagTransferViewModel.StatusText));
        return status;
    }

    private View BuildResultsArea()
    {
        var template = new DataTemplate(() =>
        {
            var title = ThemeHelper.Label(13, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", false);
            title.SetBinding(Label.TextProperty, nameof(BatchResultItem.Title));
            var status = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", false);
            status.SetBinding(Label.TextProperty, nameof(BatchResultItem.Status));

            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                },
            };
            grid.Add(title, 0);
            grid.Add(status, 1);
            return new Border
            {
                Margin = new Thickness(0, 3),
                StrokeThickness = 0,
                Background = ThemeHelper.Brush("CardBackgroundColor", "#1AFFFFFF"),
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                Padding = new Thickness(10, 8),
                Content = grid,
            };
        });

        return new CollectionView
        {
            ItemsSource = _vm.Results,
            ItemTemplate = template,
            EmptyView = new Label
            {
                Text = "运行后在此显示结果",
                TextColor = ThemeHelper.Color("TextSecondaryColor", "#C2C6E4"),
                FontSize = 13,
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 20),
            },
        };
    }
}