using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 歌曲信息页（Lyrico「Song Info」复刻）：
/// 展示音频技术详情（时长/比特率/采样率/声道/路径/大小/格式）与标签信息，
/// 底部提供「复制信息」按钮（写入剪贴板）。
/// </summary>
public class SongInfoPage : ContentPage
{
    private readonly SongInfoViewModel _vm;

    public SongInfoPage(SongItem song)
    {
        _vm = new SongInfoViewModel(song, PluginHost.AudioFiles);
        BindingContext = _vm;

        Title = "歌曲信息";
        BackgroundColor = ThemeHelper.Color("WindowBackgroundColor", "#1A1838");

        Content = new ScrollView { Content = BuildContent() };
        _ = _vm.LoadAsync();
    }

    private View BuildContent()
    {
        var root = new VerticalStackLayout { Spacing = 12, Padding = new Thickness(16, 12) };
        root.Add(BuildHeader());
        root.Add(BuildInfoCard());
        root.Add(BuildCopyButton());
        return root;
    }

    private View BuildHeader()
    {
        var title = ThemeHelper.Label(20, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
        title.SetBinding(Label.TextProperty, nameof(SongInfoViewModel.Title));
        var artist = ThemeHelper.Label(14, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        artist.SetBinding(Label.TextProperty, nameof(SongInfoViewModel.Artist));
        var album = ThemeHelper.Label(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        album.SetBinding(Label.TextProperty, nameof(SongInfoViewModel.Album));

        var stack = new VerticalStackLayout
        {
            Spacing = 4,
            HorizontalOptions = LayoutOptions.Center,
            Children = { title, artist, album },
        };
        return stack;
    }

    private View BuildInfoCard()
    {
        var fields = new (string label, string binding)[]
        {
            ("状态", nameof(SongInfoViewModel.Status)),
            ("时长", nameof(SongInfoViewModel.Duration)),
            ("比特率", nameof(SongInfoViewModel.Bitrate)),
            ("采样率", nameof(SongInfoViewModel.SampleRate)),
            ("声道", nameof(SongInfoViewModel.Channels)),
            ("音轨", nameof(SongInfoViewModel.Track)),
            ("年份", nameof(SongInfoViewModel.Year)),
            ("流派", nameof(SongInfoViewModel.Genre)),
            ("格式", nameof(SongInfoViewModel.Format)),
            ("文件大小", nameof(SongInfoViewModel.FileSize)),
            ("文件路径", nameof(SongInfoViewModel.FilePath)),
        };

        var grid = new Grid
        {
            Padding = new Thickness(14),
            RowSpacing = 10,
            ColumnSpacing = 12,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };
        for (var r = 0; r < fields.Length; r++)
        {
            var l = ThemeHelper.Label(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
            l.Text = fields[r].label;
            var v = ThemeHelper.Label(13, FontAttributes.None, "TextPrimaryColor", "#F7F8FF", true);
            if (fields[r].binding == nameof(SongInfoViewModel.FilePath))
                v.LineBreakMode = LineBreakMode.WordWrap;
            v.SetBinding(Label.TextProperty, fields[r].binding);
            grid.Add(l, 0, r);
            grid.Add(v, 1, r);
        }

        return new Border
        {
            StrokeThickness = 0,
            Background = ThemeHelper.Brush("CardBackgroundColor", "#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Content = grid,
        };
    }

    private View BuildCopyButton()
    {
        var btn = new Button
        {
            Text = "复制信息",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = ThemeHelper.Color("PrimaryColor", "#8C7BFF"),
            CornerRadius = 20,
            HeightRequest = 44,
        };
        btn.Clicked += async (_, _) =>
        {
            await Clipboard.SetTextAsync(_vm.BuildInfoText());
            await DisplayAlert("已复制", "文件信息已复制到剪贴板", "好");
        };
        return btn;
    }
}