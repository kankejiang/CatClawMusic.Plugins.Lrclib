using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using System.Collections.ObjectModel;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// Lyrico 风格音乐库主框架 ViewModel：歌曲/专辑/艺人三大 Tab，
/// 数据来自宿主 <see cref="IMusicLibraryService"/>（复用宿主已扫描的本地音乐库），
/// 支持搜索过滤与 A-Z/中文首字 字母索引分组。
/// </summary>
public partial class MusicLibraryViewModel : ObservableObject
{
    private readonly IMusicLibraryService _library;
    private readonly ArtistSplitStore _splitStore = new();

    /// <summary>当前激活的底部 Tab（0=歌曲，1=专辑，2=艺人）</summary>
    [ObservableProperty] private int activeTab;

    [ObservableProperty] private bool isLoading;

    [ObservableProperty] private string statusText = "加载中...";

    [ObservableProperty] private string searchQuery = "";

    /// <summary>歌曲分组（按索引字母）</summary>
    [ObservableProperty] private ObservableCollection<SongGroup> songGroups = new();

    /// <summary>专辑列表</summary>
    [ObservableProperty] private ObservableCollection<AlbumItem> albums = new();

    /// <summary>艺人分组（按索引字母）</summary>
    [ObservableProperty] private ObservableCollection<ArtistGroup> artistGroups = new();

    /// <summary>文件夹分组（按歌曲所在目录，再按目录名首字母分组）</summary>
    [ObservableProperty] private ObservableCollection<FolderGroup> folderGroups = new();

    /// <summary>歌曲 Tab 字母索引侧栏项</summary>
    [ObservableProperty] private ObservableCollection<LetterItem> songLetters = new();

    /// <summary>艺人 Tab 字母索引侧栏项</summary>
    [ObservableProperty] private ObservableCollection<LetterItem> artistLetters = new();

    /// <summary>文件夹 Tab 字母索引侧栏项</summary>
    [ObservableProperty] private ObservableCollection<LetterItem> folderLetters = new();

    /// <summary>当前点击的歌曲（页面据此跳转详情）</summary>
    [ObservableProperty] private SongItem? selectedSong;

    /// <summary>当前点击的专辑</summary>
    [ObservableProperty] private AlbumItem? selectedAlbum;

    /// <summary>当前点击的艺人</summary>
    [ObservableProperty] private ArtistItem? selectedArtist;

    private List<Song> _allSongs = new();
    private List<Album> _allAlbums = new();
    private List<ArtistItem> _allArtists = new();

    public MusicLibraryViewModel(IMusicLibraryService library)
    {
        _library = library;
        ArtistSplitStore.AnyChanged += OnArtistSplitChanged;
    }

    /// <summary>拆分配置变更后重建艺人分组（不重复拉取宿主，仅重新聚合+过滤）</summary>
    private void OnArtistSplitChanged()
    {
        _allArtists = AggregateArtists(_allSongs);
        ApplyFilters();
    }

    /// <summary>从宿主加载本地音乐库并构建分组</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusText = "正在加载音乐库...";
        try
        {
            // 全部在后台线程完成：宿主读取 + 艺人聚合 + 分组/列表项构建。
            // 避免在 UI 线程同步 LINQ 全量分组并重建大 ObservableCollection 造成主线程卡死。
            await Task.Run(() => BuildLibraryCoreAsync());
        }
        catch (Exception ex)
        {
            StatusText = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>后台构建音乐库分组数据，结束后切回 UI 线程一次性赋值各集合。</summary>
    private async Task BuildLibraryCoreAsync()
    {
        var songs = await _library.GetAllSongsAsync();
        var albums = await _library.GetAllAlbumsAsync();

        _allSongs = songs.Where(s => s.Source == SongSource.Local).ToList();
        _allAlbums = albums;
        var artists = AggregateArtists(_allSongs);

        // 纯对象构建（SongItem/SongGroup/AlbumItem 不依赖 UI），可安全在后台线程执行
        var songGroups = BuildSongGroups(_allSongs);
        var artistGroups = BuildArtistGroups(artists);
        var folderGroups = BuildFolderGroups(_allSongs);
        var albumItems = new ObservableCollection<AlbumItem>(_allAlbums.Select(a => new AlbumItem(a)));
        var songLetters = new ObservableCollection<LetterItem>(songGroups.Select(g => new LetterItem(g.Key)));
        var artistLetters = new ObservableCollection<LetterItem>(artistGroups.Select(g => new LetterItem(g.Key)));
        var folderLetters = new ObservableCollection<LetterItem>(folderGroups.Select(g => new LetterItem(g.Key)));

        // 只把集合引用切回 UI 线程赋值（触发绑定通知），避免主线程做大工作
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            SongGroups = songGroups;
            ArtistGroups = artistGroups;
            FolderGroups = folderGroups;
            Albums = albumItems;
            SongLetters = songLetters;
            ArtistLetters = artistLetters;
            FolderLetters = folderLetters;
            StatusText = $"共 {_allSongs.Count} 首歌曲 · {_allAlbums.Count} 张专辑 · {artists.Count} 位艺人 · {folderGroups.Sum(g => g.Count)} 个文件夹";
        });
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilters();

    partial void OnActiveTabChanged(int value)
    {
        // 切换 Tab 只刷新过滤视图，避免在构造/导航阶段同步触发加载引发的 UI 线程阻塞；
        // 首次加载统一由页面 OnAppearing 触发 LoadAsync。
        ApplyFilters();
    }

    /// <summary>按当前搜索词过滤并重建分组</summary>
    private void ApplyFilters()
    {
        var q = SearchQuery?.Trim();

        var songs = string.IsNullOrEmpty(q)
            ? _allSongs
            : _allSongs.Where(s => Matches(s.Title, s.Artist, q)).ToList();

        var artists = string.IsNullOrEmpty(q)
            ? _allArtists
            : _allArtists.Where(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        var albums = string.IsNullOrEmpty(q)
            ? _allAlbums
            : _allAlbums.Where(a => Matches(a.Title, a.Artist, q)).ToList();

        SongGroups = BuildSongGroups(songs);
        ArtistGroups = BuildArtistGroups(artists);
        Albums = new ObservableCollection<AlbumItem>(albums.Select(a => new AlbumItem(a)));
        SongLetters = new ObservableCollection<LetterItem>(SongGroups.Select(g => new LetterItem(g.Key)));
        ArtistLetters = new ObservableCollection<LetterItem>(ArtistGroups.Select(g => new LetterItem(g.Key)));
    }

    private static bool Matches(string a, string b, string q)
        => (a?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
           || (b?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);

    private static ObservableCollection<SongGroup> BuildSongGroups(List<Song> songs)
        => new(songs
            .GroupBy(s => GetIndexLetter(s.Title))
            .OrderBy(g => g.Key, new LetterComparer())
            .Select(g => new SongGroup(g.Key, g.Select(s => new SongItem(s)).ToList())));

    private static ObservableCollection<ArtistGroup> BuildArtistGroups(List<ArtistItem> artists)
        => new(artists
            .GroupBy(a => GetIndexLetter(a.Name))
            .OrderBy(g => g.Key, new LetterComparer())
            .Select(g => new ArtistGroup(g.Key, g.ToList())));

    /// <summary>按歌曲所在目录聚合文件夹，再按目录名首字母分组。</summary>
    internal static ObservableCollection<FolderGroup> BuildFolderGroups(List<Song> songs)
        => new(songs
            .Select(s => DirectoryNameOf(s.FilePath))
            .Where(d => !string.IsNullOrEmpty(d))
            .GroupBy(d => d!)
            .Select(g => new FolderItem(Path.GetFileName(g.Key) ?? g.Key, g.Key, g.Count()))
            .GroupBy(f => GetIndexLetter(f.Name))
            .OrderBy(g => g.Key, new LetterComparer())
            .Select(g => new FolderGroup(g.Key, g.ToList())));

    /// <summary>取文件所在目录（无路径/非法返回 null）。</summary>
    internal static string? DirectoryNameOf(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return null;
        try { return Path.GetDirectoryName(filePath); } catch { return null; }
    }

    /// <summary>从歌曲聚合艺人（可配置多艺人拆分：分隔符/不拆分艺人；默认 / ; , 等拆分）</summary>
    private List<ArtistItem> AggregateArtists(List<Song> songs)
    {
        var splitConfig = _splitStore.Get();
        return songs
            .SelectMany(s => ArtistNameSplitter.SplitArtists(s.Artist, splitConfig))
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .GroupBy(a => a, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ArtistItem(g.First(), g.Count()))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>取索引字母：英文取首字母大写，数字归 #，中文直接取首字（与宿主艺术家页一致）</summary>
    internal static string GetIndexLetter(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "#";
        var c = name.Trim()[0];
        if (char.IsAsciiLetter(c)) return char.ToUpperInvariant(c).ToString();
        if (char.IsDigit(c)) return "#";
        return c.ToString();
    }

    /// <summary>字母排序：A-Z → 中文 → #</summary>
    internal sealed class LetterComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return 1;
            if (y == null) return -1;

            int rank(string s) => s switch
            {
                "#" => 2,
                _ when s.Length == 1 && char.IsAsciiLetter(s[0]) => 0,
                _ => 1
            };

            int rx = rank(x), ry = rank(y);
            if (rx != ry) return rx.CompareTo(ry);
            return string.Compare(x, y, StringComparison.Ordinal);
        }
    }
}

/// <summary>歌曲列表项（包装宿主 Song）</summary>
public class SongItem
{
    public Song Song { get; }
    public SongItem(Song song) => Song = song;

    public string Title => Song.Title;
    public string Artist => Song.Artist;
    public string DurationText => ThemeHelper.FormatDuration(Song.Duration);
    public string CoverText => Song.Title;
    public string? CoverPath => Song.CoverArtPath;
    public string FilePath => Song.FilePath;
}

/// <summary>歌曲分组（字母索引组头）</summary>
public class SongGroup : List<SongItem>
{
    public string Key { get; }
    public string CountText => $"共 {Count} 首";
    public SongGroup(string key, IEnumerable<SongItem> items) : base(items) => Key = key;
}

/// <summary>专辑列表项（包装宿主 Album）</summary>
public class AlbumItem
{
    public Album Album { get; }
    public AlbumItem(Album album) => Album = album;

    public string Title => Album.Title;
    public string Artist => Album.Artist;
    public string Subtitle => $"{Album.Artist} · {Album.SongCount} 首";
    public string CoverText => Album.Title;
    public string? CoverPath => Album.CoverArtPath;
}

/// <summary>艺人列表项</summary>
public class ArtistItem
{
    public string Name { get; }
    public int SongCount { get; }
    public ArtistItem(string name, int count) { Name = name; SongCount = count; }

    public string Subtitle => $"{SongCount} 首";
    public string CoverText => Name;
}

/// <summary>艺人分组（字母索引组头）</summary>
public class ArtistGroup : List<ArtistItem>
{
    public string Key { get; }
    public string CountText => $"共 {Count} 位";
    public ArtistGroup(string key, IEnumerable<ArtistItem> items) : base(items) => Key = key;
}

/// <summary>文件夹列表项</summary>
public class FolderItem
{
    public string Name { get; }
    public string Path { get; }
    public int SongCount { get; }
    public FolderItem(string name, string path, int songCount) { Name = name; Path = path; SongCount = songCount; }

    public string Subtitle => $"{SongCount} 首 · {Path}";
    public string CoverText => Name;
}

/// <summary>文件夹分组（字母索引组头）</summary>
public class FolderGroup : List<FolderItem>
{
    public string Key { get; }
    public string CountText => $"共 {Count} 个";
    public FolderGroup(string key, IEnumerable<FolderItem> items) : base(items) => Key = key;
}

/// <summary>字母索引侧栏项</summary>
public class LetterItem
{
    public string Key { get; }
    public string Label => Key;
    public LetterItem(string key) => Key = key;
}
