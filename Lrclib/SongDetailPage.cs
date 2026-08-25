using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 歌曲详情页：封面 + 基础信息 + 文件内标签概览，
/// 底部操作：编辑标签 / 搜索歌词 / 搜索封面（进入 Lyrico 式补全流程）。
/// </summary>
public class SongDetailPage : ContentPage
{
    private readonly SongDetailViewModel _vm;

    public SongDetailPage(SongItem song)
    {
        _vm = new SongDetailViewModel(song, PluginHost.AudioFiles);
        BindingContext = _vm;

        Title = "歌曲详情";
        BackgroundColor = ThemeHelper.Color("WindowBackgroundColor", "#1A1838");

        var scroll = new ScrollView { Content = BuildContent() };
        Content = scroll;

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        await _vm.LoadAsync();
    }

    private View BuildContent()
    {
        var root = new VerticalStackLayout { Spacing = 12, Padding = new Thickness(16, 12) };

        // ── 头部：封面 + 标题/艺人/专辑 ──
        root.Add(BuildHeader());

        // ── 文件内标签信息卡 ──
        root.Add(BuildTagCard());

        // ── 歌词预览 ──
        root.Add(BuildLyricsCard());

        // ── 操作按钮 ──
        root.Add(BuildActions());

        return root;
    }

    private View BuildHeader()
    {
        var title = ThemeHelper.Label(20, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
        title.SetBinding(Label.TextProperty, nameof(SongDetailViewModel.TagTitle));
        var artist = ThemeHelper.Label(14, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        artist.SetBinding(Label.TextProperty, nameof(SongDetailViewModel.TagArtist));
        var album = ThemeHelper.Label(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        album.SetBinding(Label.TextProperty, nameof(SongDetailViewModel.TagAlbum));

        var cover = new Image
        {
            HeightRequest = 140,
            WidthRequest = 140,
            Aspect = Aspect.AspectFill,
        };
        cover.SetBinding(Image.SourceProperty, nameof(SongDetailViewModel.CoverSource));

        var placeholderLabel = new Label
        {
            Text = _vm.Song.Title.Trim().Length > 0 ? _vm.Song.Title.Trim()[..1].ToUpperInvariant() : "♪",
            FontSize = 44,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        placeholderLabel.SetDynamicResource(Label.TextColorProperty, "PrimaryColor");

        var placeholder = new Border
        {
            HeightRequest = 140,
            WidthRequest = 140,
            StrokeThickness = 0,
            Background = ThemeHelper.Brush("CardBackgroundStrongColor", "#2DFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18) },
            Content = placeholderLabel,
        };

        var coverBox = new Grid
        {
            HeightRequest = 140,
            WidthRequest = 140,
            HorizontalOptions = LayoutOptions.Center,
            Children = { placeholder, cover },
        };

        var textStack = new VerticalStackLayout
        {
            Spacing = 4,
            HorizontalOptions = LayoutOptions.Center,
            Children = { title, artist, album },
        };

        return new VerticalStackLayout
        {
            Spacing = 12,
            Children = { coverBox, textStack },
        };
    }

    private View BuildTagCard()
    {
        var status = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        status.SetBinding(Label.TextProperty, nameof(SongDetailViewModel.TagStatus));
        var fileInfo = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        fileInfo.SetBinding(Label.TextProperty, nameof(SongDetailViewModel.FileInfo));

        var grid = new Grid
        {
            Padding = new Thickness(14),
            RowSpacing = 8,
            ColumnSpacing = 12,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };

        AddField(grid, 0, "年份", nameof(SongDetailViewModel.TagYear));
        AddField(grid, 1, "流派", nameof(SongDetailViewModel.TagGenre));
        AddField(grid, 2, "音轨", nameof(SongDetailViewModel.TagTrack));
        AddField(grid, 3, "文件", nameof(SongDetailViewModel.FileInfo));
        grid.Add(status, 1, 4);

        return new Border
        {
            StrokeThickness = 0,
            Background = ThemeHelper.Brush("CardBackgroundColor", "#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Content = grid,
        };
    }

    private static void AddField(Grid grid, int row, string label, string binding)
    {
        var l = ThemeHelper.Label(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        l.Text = label;
        var v = ThemeHelper.Label(13, FontAttributes.None, "TextPrimaryColor", "#F7F8FF", true);
        v.SetBinding(Label.TextProperty, binding);
        grid.Add(l, 0, row);
        grid.Add(v, 1, row);
    }

    private View BuildLyricsCard()
    {
        var title = ThemeHelper.Label(14, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", false);
        title.Text = "内嵌歌词";
        var preview = ThemeHelper.Label(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", false);
        preview.MaxLines = 6;
        preview.SetBinding(Label.TextProperty, nameof(SongDetailViewModel.TagLyricsPreview));
        var empty = ThemeHelper.Label(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", false);
        empty.Text = "无内嵌歌词，可点击「搜索歌词」在线补全";
        empty.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(SongDetailViewModel.HasLyrics))
        {
            Converter = new InvertBoolConverter(),
        });
        preview.SetBinding(VisualElement.IsVisibleProperty, nameof(SongDetailViewModel.HasLyrics));

        return new Border
        {
            StrokeThickness = 0,
            Background = ThemeHelper.Brush("CardBackgroundColor", "#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Padding = new Thickness(14),
            Content = new VerticalStackLayout { Spacing = 8, Children = { title, preview, empty } },
        };
    }

    private View BuildActions()
    {
        var edit = MakeActionButton("编辑标签", () => PluginNav.PushAsync(new EditMetadataPage(_vm.Song)));
        var lyrics = MakeActionButton("搜索歌词", () => PluginNav.PushAsync(new SearchLyricsPage(_vm.Song)));
        var cover = MakeActionButton("搜索封面", () => PluginNav.PushAsync(new SearchCoverPage(_vm.Song)));
        var info = MakeActionButton("歌曲信息", () => PluginNav.PushAsync(new SongInfoPage(_vm.Song)));

        var grid = new Grid
        {
            RowSpacing = 10,
            ColumnSpacing = 10,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
        };
        grid.Add(edit, 0, 0);
        grid.Add(lyrics, 1, 0);
        grid.Add(cover, 2, 0);
        grid.Add(info, 0, 1);
        return grid;
    }

    private static Button MakeActionButton(string text, Action onClick)
    {
        var b = new Button
        {
            Text = text,
            FontSize = 13,
            TextColor = Colors.White,
            BackgroundColor = ThemeHelper.Color("PrimaryColor", "#8C7BFF"),
            CornerRadius = 16,
            HeightRequest = 44,
        };
        b.Clicked += (_, _) => onClick();
        return b;
    }
}

/// <summary>bool 取反（用于「有歌词」→ 隐藏预览 / 显示空提示）</summary>
internal class InvertBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
