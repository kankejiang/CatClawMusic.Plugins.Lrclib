using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 文件夹详情页：列出某目录下的全部本地歌曲（目录名 + 歌曲数），点击进入歌曲详情。
/// 数据来自宿主已扫描的本地音乐库（按歌曲文件所在目录过滤）。
/// </summary>
public class FolderDetailPage : ContentPage
{
    private readonly FolderItem _folder;
    private readonly ObservableCollection<SongItem> _songs = new();
    private readonly CollectionView _list;
    private readonly Label _empty;
    private bool _loaded;

    public FolderDetailPage(FolderItem folder)
    {
        _folder = folder;
        Title = folder.Name;
        BackgroundColor = ThemeHelper.Color("WindowBackgroundColor", "#1A1838");

        var subtitle = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        subtitle.Text = $"{folder.Path} · {folder.SongCount} 首";
        subtitle.Margin = new Thickness(14, 8, 14, 0);

        _list = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            Margin = new Thickness(10, 6, 10, 0),
            ItemTemplate = new DataTemplate(LyricoUi.SongRow),
        };
        _list.ItemsSource = _songs;
        _list.SelectionChanged += OnSelected;

        _empty = ThemeHelper.Label(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        _empty.Text = "该目录下没有本地歌曲";
        _empty.HorizontalOptions = LayoutOptions.Center;
        _empty.Margin = new Thickness(0, 16, 0, 0);
        _empty.IsVisible = false;

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
        };
        root.Add(subtitle, 0, 0);
        root.Add(_list, 0, 1);
        root.Add(_empty, 0, 1);   // 与列表同行，互斥显示
        Content = root;
        KickOffLoad();
    }

    /// <summary>构造期即启动加载：桌面模态导航 WrapRoot 会吞掉首推页的 OnAppearing，不能依赖它触发。</summary>
    private void KickOffLoad()
    {
        if (_loaded || PluginHost.Library is not IMusicLibraryService lib) return;
        _loaded = true;
        _ = LoadAsync(lib);
    }

    private async Task LoadAsync(IMusicLibraryService lib)
    {
        try
        {
            var localDir = _folder.Path;
            var all = await lib.GetAllSongsAsync() ?? new List<Song>();
            var songs = all
                .Where(s => s.Source == SongSource.Local
                    && string.Equals(MusicLibraryViewModel.DirectoryNameOf(s.FilePath), localDir, StringComparison.OrdinalIgnoreCase))
                .Select(s => new SongItem(s))
                .ToList();
            foreach (var s in songs) _songs.Add(s);
            if (_songs.Count == 0) _empty.IsVisible = true;
        }
        catch { }
    }

    private async void OnSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SongItem song) return;
        _list.SelectedItem = null;
        await PluginNav.PushAsync(new SongDetailPage(song));
    }
}