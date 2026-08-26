using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>专辑详情页：封面 + 专辑信息 + 专辑内歌曲列表（点歌曲进详情）</summary>
public class AlbumDetailPage : ContentPage
{
    private readonly AlbumItem _album;
    private readonly CollectionView _list = new()
    {
        SelectionMode = SelectionMode.Single,
    };

    public AlbumDetailPage(AlbumItem album)
    {
        _album = album;
        BindingContext = album;   // 页头绑定依赖（Title/Subtitle/CoverPath/CoverText）
        Title = album.Title;
        BackgroundColor = ThemeHelper.Color("WindowBackgroundColor", "#1A1838");

        _list.ItemTemplate = new DataTemplate(LyricoUi.SongRow);
        _list.SelectionChanged += OnSongSelected;

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
        };
        root.Add(BuildHeader(), 0, 0);
        root.Add(_list, 0, 1);
        Content = root;
        KickOffLoad();
    }

    private bool _loaded;

    /// <summary>构造期即启动加载：桌面模态导航 WrapRoot 会吞掉首推页的 OnAppearing，不能依赖它触发。</summary>
    private void KickOffLoad()
    {
        if (_loaded) return;
        _loaded = true;
        _ = LoadAsync();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        KickOffLoad();
    }

    private async Task LoadAsync()
    {
        var library = PluginHost.Library;
        if (library is null) return;

        var songs = await library.GetSongsByAlbumAsync(_album.Title);
        var items = songs
            .Where(s => s.Source == CatClawMusic.Core.Models.SongSource.Local)
            .Select(s => new SongItem(s))
            .ToList();
        _list.ItemsSource = items;
    }

    private View BuildHeader()
    {
        var title = ThemeHelper.Label(18, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
        title.SetBinding(Label.TextProperty, nameof(AlbumItem.Title));
        var subtitle = ThemeHelper.Label(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        subtitle.SetBinding(Label.TextProperty, nameof(AlbumItem.Subtitle));

        var stack = new VerticalStackLayout
        {
            Spacing = 6,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                LyricoUi.Cover(nameof(AlbumItem.CoverPath), nameof(AlbumItem.CoverText), 180, 20),
                title,
                subtitle,
            },
        };

        var box = new Grid { Padding = new Thickness(16, 16, 16, 12) };
        box.Add(stack, 0);
        return box;
    }

    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SongItem song) return;
        _list.SelectedItem = null;
        await PluginNav.PushAsync(new SongDetailPage(song));
    }
}
