using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 批量响度 ReplayGain 页（复刻 Lyrico 批量响度场景）：
/// 逐首分析 EBU R128 响度并把 ReplayGain 增益写回标签。
/// 宿主 <see cref="ILoudnessAnalyzer"/> 不可用（桌面/无 FFmpeg）时展示降级提示。
/// </summary>
public class BatchReplayGainPage : ContentPage
{
    private readonly BatchReplayGainViewModel _vm;

    public BatchReplayGainPage(IReadOnlyList<SongItem> songs)
    {
        _vm = new BatchReplayGainViewModel(songs, PluginHost.AudioFiles, PluginHost.Get<ILoudnessAnalyzer>());
        BindingContext = _vm;

        Title = "批量响度";
        BackgroundColor = ThemeHelper.Color("WindowBackgroundColor", "#1A1838");
        Content = BuildContent();
    }

    private View BuildContent()
    {
        var hint = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", false);
        hint.Text = "逐首按 EBU R128 扫描响度，计算 ReplayGain 增益并写入 REPLAYGAIN_TRACK_GAIN / REPLAYGAIN_TRACK_PEAK（ID3 TXXX）。";

        var status = ThemeHelper.Label(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", false);
        status.SetBinding(Label.TextProperty, nameof(BatchReplayGainViewModel.StatusText));

        var run = new Button
        {
            Text = "开始扫描并写入",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = ThemeHelper.Color("PrimaryColor", "#8C7BFF"),
            CornerRadius = 20,
            HeightRequest = 44,
            Margin = new Thickness(0, 8, 0, 0),
        };
        run.SetBinding(Button.CommandProperty, nameof(BatchReplayGainViewModel.RunCommand));

        var configCard = new Border
        {
            Margin = new Thickness(12, 8, 12, 4),
            StrokeThickness = 0,
            Background = ThemeHelper.Brush("CardBackgroundColor", "#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Padding = new Thickness(14),
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children = { hint, status, run },
            },
        };

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
        };
        root.Add(configCard, 0);
        root.Add(BuildResultsArea(), 0, 1);
        return root;
    }

    private View BuildResultsArea()
    {
        var template = new DataTemplate(() =>
        {
            var title = ThemeHelper.Label(13, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", false);
            title.SetBinding(Label.TextProperty, nameof(BatchResultItem.Title));

            var status = new Label
            {
                FontSize = 12,
                HorizontalOptions = LayoutOptions.End,
            };
            status.SetBinding(Label.TextProperty, nameof(BatchResultItem.Status));
            status.SetBinding(Label.TextColorProperty, new Binding(nameof(BatchResultItem.StatusColor))
            {
                Converter = new HexToColorConverter(),   // hex 字符串 → Color，缺省则绑定静默失败
            });

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