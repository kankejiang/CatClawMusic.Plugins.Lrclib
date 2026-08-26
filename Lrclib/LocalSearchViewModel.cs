using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>本地搜索 Tab</summary>
public enum LocalSearchTab
{
    All,
    Songs,
    Albums,
    Artists,
    Lyrics,
}

/// <summary>
/// 本地搜索 ViewModel（Lyrico LocalSearchScreen 复刻）：
/// 在宿主已扫描的音乐库内，按关键词同时匹配 歌曲（标题/艺人/专辑/文件名）、专辑、艺人 与 歌词内容。
/// </summary>
public partial class LocalSearchViewModel : ObservableObject
{
    private readonly IMusicLibraryService _library;
    private readonly IAudioFileService? _audio;

    [ObservableProperty] private string query = "";
    [ObservableProperty] private int activeTab;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isLyricSearching;
    [ObservableProperty] private int lyricScanTotal;
    [ObservableProperty] private int lyricScanDone;

    /// <summary>分区混排结果流（SectionHeader / SongItem / AlbumItem / ArtistItem / LyricMatchItem）</summary>
    [ObservableProperty] private ObservableCollection<object> results = new();

    private List<Song> _allSongs = new();
    private List<Album> _allAlbums = new();
    private List<ArtistItem> _allArtists = new();
    private CancellationTokenSource? _lyricCts;

    /// <summary>最近一次后台歌词扫描的完整结果（供 All tab 即时并入）。</summary>
    private List<LyricMatchItem> PendingLyricMatches = new();
    /// <summary>PendingLyricMatches 所属的查询词（防止旧查询结果混入新查询）。</summary>
    private string _pendingQuery = "";

    /// <summary>歌词全文缓存（FilePath → 歌词文本），避免搜索时重复读取。</summary>
    private static readonly Dictionary<string, string> LyricCache = new();

    public LocalSearchViewModel(IMusicLibraryService library)
    {
        _library = library;
        _audio = PluginHost.AudioFiles;
    }

    partial void OnQueryChanged(string value) => Apply();

    partial void OnActiveTabChanged(int value) => Apply();

    /// <summary>首次进入页面时预载音乐库快照并做一次全量搜索。</summary>
    [RelayCommand]
    public async Task InitializeAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            if (_allSongs.Count == 0)
            {
                var songs = await Task.Run(() => _library.GetAllSongsAsync());
                var albums = await Task.Run(() => _library.GetAllAlbumsAsync());
                _allSongs = songs.Where(s => s.Source == SongSource.Local && !string.IsNullOrWhiteSpace(s.FilePath)).ToList();
                _allAlbums = albums;
                _allArtists = AggregateArtists(_allSongs);
            }
            Apply();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>按当前 Tab 与关键词重建扁平结果流（分区头 + 各项）。</summary>
    private void Apply()
    {
        var q = Query?.Trim() ?? "";
        var needLyrics = q.Length > 0 && (ActiveTab == (int)LocalSearchTab.All || ActiveTab == (int)LocalSearchTab.Lyrics);

        var list = new List<object>();

        if (ActiveTab == (int)LocalSearchTab.All || ActiveTab == (int)LocalSearchTab.Songs)
        {
            var songs = FilterSongs(_allSongs, q);
            AddSongsSection(list, songs);
        }

        if (ActiveTab == (int)LocalSearchTab.All || ActiveTab == (int)LocalSearchTab.Albums)
            AddAlbumsSection(list, FilterAlbums(_allAlbums, q));

        if (ActiveTab == (int)LocalSearchTab.All || ActiveTab == (int)LocalSearchTab.Artists)
            AddArtistsSection(list, FilterArtists(_allArtists, q));

        // 歌词分区：All / Lyrics tab 且有关键词时后台扫描，已完成结果先并入；扫描中显示状态项
        if (needLyrics)
        {
            // 该查询已扫描完成则只渲染结果，不再重启扫描（否则完成回调→Apply→重扫 死循环）
            var alreadyDone = _pendingQuery == q && !IsLyricSearching;
            if (!alreadyDone)
                _ = SearchLyricsAsync(q);

            var showSection = ActiveTab == (int)LocalSearchTab.All || ActiveTab == (int)LocalSearchTab.Lyrics;
            if (showSection)
            {
                // 只并入属于当前查询的结果（防止上一轮扫描的旧结果混入）
                if (_pendingQuery == q)
                {
                    var status = IsLyricSearching
                        ? $"匹配中，请稍候…（{LyricScanDone}/{LyricScanTotal}）"
                        : PendingLyricMatches.Count > 0 ? $"{PendingLyricMatches.Count} 首命中" : "无匹配歌词";
                    list.Add(new SectionHeaderItem("歌词", status));
                    foreach (var m in PendingLyricMatches) list.Add(m);
                }
                else
                {
                    list.Add(new SectionHeaderItem("歌词", "匹配中，请稍候…"));
                }
            }
        }
        else if (ActiveTab == (int)LocalSearchTab.Lyrics)
        {
            list.Add(new SectionHeaderItem("歌词", "输入关键词开始搜索歌词"));
        }

        Results = new ObservableCollection<object>(list);
    }

    private static void AddSongsSection(List<object> list, List<SongItem> songs)
    {
        if (songs.Count == 0) return;
        list.Add(new SectionHeaderItem("歌曲", $"{songs.Count} 首"));
        list.AddRange(songs);
    }

    private static void AddAlbumsSection(List<object> list, List<AlbumItem> albums)
    {
        if (albums.Count == 0) return;
        list.Add(new SectionHeaderItem("专辑", $"{albums.Count} 张"));
        list.AddRange(albums);
    }

    private static void AddArtistsSection(List<object> list, List<ArtistItem> artists)
    {
        if (artists.Count == 0) return;
        list.Add(new SectionHeaderItem("艺人", $"{artists.Count} 位"));
        list.AddRange(artists);
    }

    private static List<SongItem> FilterSongs(List<Song> songs, string q)
        => songs
            .Where(s => MatchIn(s.Title, q) || MatchIn(s.Artist, q) || MatchIn(s.Album, q) || MatchIn(FileNameOf(s.FilePath), q))
            .Select(s => new SongItem(s))
            .Take(300)
            .ToList();

    private static List<AlbumItem> FilterAlbums(List<Album> albums, string q)
        => albums
            .Where(a => MatchIn(a.Title, q) || MatchIn(a.Artist, q) || MatchIn(a.Name, q))
            .Select(a => new AlbumItem(a))
            .Take(200)
            .ToList();

    private static List<ArtistItem> FilterArtists(List<ArtistItem> artists, string q)
        => artists.Where(a => MatchIn(a.Name, q)).ToList();

    private static bool MatchIn(string? s, string q)
        => !string.IsNullOrWhiteSpace(s) && s.Contains(q, StringComparison.OrdinalIgnoreCase);

    private static string FileNameOf(string path)
    {
        try { return Path.GetFileName(path) ?? ""; }
        catch { return ""; }
    }

    /// <summary>
    /// 歌词内容搜索：对每首本地歌曲读取歌词全文（内嵌优先，侧车 .lrc 兜底），
    /// 匹配关键词时生成带匹配行预览的结果。串行执行以复用缓存、减少并发读盘。
    /// </summary>
    private async Task SearchLyricsAsync(string q)
    {
        _lyricCts?.Cancel();
        var cts = new CancellationTokenSource();
        _lyricCts = cts;
        IsLyricSearching = true;
        LyricScanTotal = _allSongs.Count;
        LyricScanDone = 0;

        var matches = new List<LyricMatchItem>();
        try
        {
            foreach (var song in _allSongs)
            {
                if (cts.IsCancellationRequested) break;
                var text = await GetLyricTextAsync(song);
                LyricScanDone++;

                if (string.IsNullOrEmpty(text)) continue;
                var line = text.Split('\n')
                    .Select(StripTimestamp)
                    .FirstOrDefault(l => MatchIn(l, q));
                if (line != null)
                    matches.Add(new LyricMatchItem(new SongItem(song), line.Trim()));
            }

            if (!cts.IsCancellationRequested)
            {
                _pendingQuery = q;
                PendingLyricMatches = matches;
            }
        }
        catch { /* 取消中断等静默 */ }
        finally
        {
            IsLyricSearching = false;
            // 扫描完成后重跑 Apply 把结果回流到列表。
            // 必须在 IsLyricSearching=false 之后调用：Apply 会以"该查询已完成"判定跳过重新扫描，避免死循环。
            if (!cts.IsCancellationRequested && Query == q) Apply();
        }
    }

    /// <summary>读取单曲歌词全文：先走缓存，再读内嵌标签，最后读侧车 .lrc。</summary>
    private async Task<string> GetLyricTextAsync(Song song)
    {
        var fp = song.FilePath ?? "";
        if (fp.Length == 0) return "";
        lock (LyricCache)
            if (LyricCache.TryGetValue(fp, out var cached)) return cached;

        string text = "";
        try
        {
            if (_audio != null)
            {
                var tag = await _audio.ReadTagsAsync(fp);
                text = tag?.Lyrics ?? "";
            }
        }
        catch { text = ""; }

        if (string.IsNullOrEmpty(text) && !string.IsNullOrWhiteSpace(song.LyricsPath)
            && !song.LyricsPath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(song.LyricsPath))
                    text = System.IO.File.ReadAllText(song.LyricsPath, System.Text.Encoding.UTF8);
            }
            catch { text = ""; }
        }

        lock (LyricCache)
        {
            LyricCache[song.FilePath!] = text;
            if (LyricCache.Count > 500)
            {
                var keys = LyricCache.Keys.Take(200).ToList();
                foreach (var k in keys) LyricCache.Remove(k);
            }
        }
        return text;
    }

    /// <summary>去掉 LRC 时间轴前缀 [mm:ss.xx] 以匹配纯文本。</summary>
    private static readonly System.Text.RegularExpressions.Regex TimestampRegex =
        new(@"^\[(?:\d{1,3}:)?\d{1,2}([:.]\d{1,3})*\]", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string StripTimestamp(string line)
    {
        var s = line.TrimStart();
        while (s.StartsWith("["))
        {
            var m = TimestampRegex.Match(s);
            if (!m.Success) break;
            s = s[m.Length..].TrimStart();
        }
        return s;
    }

    /// <summary>从歌曲聚合艺人（多艺人名分隔拆分去重）。</summary>
    private static List<ArtistItem> AggregateArtists(List<Song> songs)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in songs)
        {
            foreach (var name in (s.Artist ?? "").Split(new[] { " / ", "/", "；", ";", "," },
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (name.Length == 0) continue;
                result.TryGetValue(name, out var c);
                result[name] = c + 1;
            }
        }
        return result.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new ArtistItem(kv.Key, kv.Value))
            .ToList();
    }
}

/// <summary>歌词匹配结果项：歌曲 + 命中的那一行歌词预览。</summary>
public class LyricMatchItem
{
    public SongItem Song { get; }
    public string LyricLine { get; }
    public LyricMatchItem(SongItem song, string lyricLine)
    {
        Song = song;
        LyricLine = lyricLine;
    }
}

/// <summary>搜索结果分区头（如「歌曲 12 首」）。</summary>
public class SectionHeaderItem
{
    public string Title { get; }
    public string Subtitle { get; }
    public SectionHeaderItem(string title, string subtitle)
    {
        Title = title;
        Subtitle = subtitle;
    }
}