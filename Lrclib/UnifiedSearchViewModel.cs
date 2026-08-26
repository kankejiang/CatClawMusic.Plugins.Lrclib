using System.Collections.ObjectModel;
using System.Text;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Plugins.Lrclib.Lyrico;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 统一搜索 ViewModel：并行搜索歌词（LRCLIB + Lyrico 多源）和封面（iTunes），
/// 结果按歌名+艺人匹配度合并展示，选中后可一键写入歌词 + 封面。
/// </summary>
public partial class UnifiedSearchViewModel : ObservableObject
{
    private readonly LrclibApiClient _lrclib;
    private readonly ItunesApiClient _itunes;
    private readonly IAudioFileService? _audio;
    private readonly LyricoLyricsHub? _lyricoHub;

    public SongItem Song { get; }

    [ObservableProperty] private string searchTitle = "";
    [ObservableProperty] private string searchArtist = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusText = string.Empty;

    /// <summary>合并后的搜索结果列表</summary>
    public ObservableCollection<UnifiedSearchResult> Results { get; } = new();

    public UnifiedSearchViewModel(SongItem song, LrclibApiClient lrclib, ItunesApiClient itunes,
        IAudioFileService? audio, LyricoLyricsHub? lyricoHub = null)
    {
        Song = song;
        _lrclib = lrclib;
        _itunes = itunes;
        _audio = audio;
        _lyricoHub = lyricoHub;
        SearchTitle = song.Title;
        SearchArtist = song.Artist;
    }

    /// <summary>并行搜索歌词和封面，合并结果。</summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        var title = SearchTitle?.Trim() ?? "";
        var artist = SearchArtist?.Trim() ?? "";
        if (string.IsNullOrEmpty(title))
        {
            StatusText = "请输入歌名";
            return;
        }

        IsBusy = true;
        StatusText = "搜索中...";
        Results.Clear();

        var lrclibTask = Task.Run(async () =>
        {
            try { return await _lrclib.SearchAsync(title, artist); }
            catch { return null; }
        });

        var itunesTask = Task.Run(async () =>
        {
            try { return await _itunes.SearchAsync(title, artist); }
            catch { return null; }
        });

        var lyricoTask = _lyricoHub != null ? Task.Run<List<(LrclibTrack track, string source)>>(async () =>
        {
            try
            {
                var duration = Song.Song.Duration > 1000 ? Song.Song.Duration / 1000.0 : Song.Song.Duration;
                var hits = await _lyricoHub.SearchAllSourcesAsync(title, artist, null, duration);
                var result = new List<(LrclibTrack, string)>();
                foreach (var entry in hits)
                {
                    var t = LyricoToLrclibTrack(entry.Name, entry.Lyrics, title, artist);
                    if (t != null) result.Add((t, entry.Name));
                }
                return result;
            }
            catch { return new List<(LrclibTrack, string)>(); }
        }) : Task.FromResult(new List<(LrclibTrack, string)>());

        await Task.WhenAll(lrclibTask, itunesTask, lyricoTask);

        var lrclibResults = lrclibTask.Result ?? new List<LrclibTrack>();
        var itunesResults = itunesTask.Result ?? new List<ItunesTrack>();
        var lyricoResults = lyricoTask.Result ?? new List<(LrclibTrack track, string source)>();

        // 合并歌词候选
        var allLyrics = new List<(LrclibTrack track, string source)>();
        foreach (var t in lrclibResults.Take(30))
            allLyrics.Add((t, "LRCLIB"));
        foreach (var (t, src) in lyricoResults.Take(30))
            allLyrics.Add((t, src));

        // 构建封面索引（按 歌名+艺人 模糊匹配）
        var coverLookup = new Dictionary<string, ItunesTrack>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in itunesResults)
        {
            var key = $"{c.TrackName?.Trim()}|{c.ArtistName?.Trim()}";
            coverLookup.TryAdd(key, c);
        }

        // 以歌词候选为主干，匹配封面
        var merged = new List<UnifiedSearchResult>();
        var usedCovers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (track, source) in allLyrics)
        {
            var key = $"{track.TrackName?.Trim()}|{track.ArtistName?.Trim()}";
            ItunesTrack? cover = null;
            if (coverLookup.TryGetValue(key, out var c))
            {
                cover = c;
                usedCovers.Add(key);
            }
            else
            {
                // 模糊匹配：只按歌名
                cover = itunesResults.FirstOrDefault(it =>
                    string.Equals(it.TrackName?.Trim(), track.TrackName?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (cover != null) usedCovers.Add($"{cover.TrackName?.Trim()}|{cover.ArtistName?.Trim()}");
            }

            merged.Add(new UnifiedSearchResult
            {
                Title = track.TrackName ?? "",
                Artist = track.ArtistName ?? "",
                Album = track.AlbumName ?? "",
                Duration = (int)track.Duration,
                Source = source,
                HasLyrics = !string.IsNullOrWhiteSpace(track.SyncedLyrics) || !string.IsNullOrWhiteSpace(track.PlainLyrics),
                LyricsType = !string.IsNullOrWhiteSpace(track.SyncedLyrics) ? "同步歌词" :
                             (!string.IsNullOrWhiteSpace(track.PlainLyrics) ? "纯文本" : "无歌词"),
                CoverUrl = cover?.ArtworkUrl100 ?? "",
                HighResCoverUrl = cover != null && !string.IsNullOrWhiteSpace(cover.ArtworkUrl100)
                    ? cover.ArtworkUrl100.Replace("100x100bb", "600x600bb") : "",
                HasCover = cover != null && !string.IsNullOrWhiteSpace(cover.ArtworkUrl100),
                LyricsTrack = track,
                CoverTrack = cover,
            });
        }

        // 剩余只有封面没有歌词的，也补上
        foreach (var c in itunesResults)
        {
            var key = $"{c.TrackName?.Trim()}|{c.ArtistName?.Trim()}";
            if (usedCovers.Contains(key)) continue;
            merged.Add(new UnifiedSearchResult
            {
                Title = c.TrackName ?? "",
                Artist = c.ArtistName ?? "",
                Album = c.CollectionName ?? "",
                Duration = 0,
                Source = "iTunes",
                HasLyrics = false,
                LyricsType = "无歌词",
                CoverUrl = c.ArtworkUrl100 ?? "",
                HighResCoverUrl = !string.IsNullOrWhiteSpace(c.ArtworkUrl100)
                    ? c.ArtworkUrl100.Replace("100x100bb", "600x600bb") : "",
                HasCover = !string.IsNullOrWhiteSpace(c.ArtworkUrl100),
                LyricsTrack = null,
                CoverTrack = c,
            });
        }

        foreach (var r in merged.Take(50))
            Results.Add(r);

        IsBusy = false;
        var total = Results.Count;
        var withLyrics = Results.Count(r => r.HasLyrics);
        var withCover = Results.Count(r => r.HasCover);
        StatusText = total == 0
            ? "没有找到结果（检查关键字，或该歌未被收录）"
            : $"找到 {total} 个结果（歌词 {withLyrics} / 封面 {withCover}）";
    }

    /// <summary>Lyrico LrcLyrics → LrclibTrack（把时间轴行序列化成 LRC 字符串，复用结果展示/写入管线）。</summary>
    private static LrclibTrack? LyricoToLrclibTrack(string sourceName, LrcLyrics lyrics, string title, string? artist)
    {
        if (lyrics.Lines.Count == 0) return null;
        var sb = new StringBuilder();
        foreach (var line in lyrics.Lines)
        {
            var t = line.Timestamp;
            sb.Append($"[{(int)t.TotalMinutes:D2}:{t.Seconds:D2}.{t.Milliseconds:D3}]")
              .Append(line.Text)
              .Append('\n');
        }
        if (sb.Length == 0) return null;

        return new LrclibTrack
        {
            TrackName = string.IsNullOrWhiteSpace(lyrics.Metadata?.Title) ? title : lyrics.Metadata!.Title,
            ArtistName = string.IsNullOrWhiteSpace(lyrics.Metadata?.Artist) ? (artist ?? "") : lyrics.Metadata!.Artist,
            AlbumName = lyrics.Metadata?.Album,
            SyncedLyrics = sb.ToString(),
        };
    }

    /// <summary>点击结果直接写入歌词 + 封面到文件。</summary>
    [RelayCommand]
    private async Task ApplyAsync(UnifiedSearchResult? item)
    {
        if (item == null || _audio == null) return;

        var writeLyrics = item.HasLyrics && item.LyricsTrack != null;
        var writeCover = item.HasCover && !string.IsNullOrWhiteSpace(item.HighResCoverUrl);

        if (!writeLyrics && !writeCover)
        {
            StatusText = "该结果无可写入内容";
            return;
        }

        IsBusy = true;
        var okCount = 0;
        try
        {
            string? lyrics = null;
            byte[]? coverBytes = null;

            if (writeLyrics && item.LyricsTrack != null)
            {
                lyrics = !string.IsNullOrWhiteSpace(item.LyricsTrack.SyncedLyrics)
                    ? item.LyricsTrack.SyncedLyrics
                    : item.LyricsTrack.PlainLyrics;
                if (!string.IsNullOrWhiteSpace(lyrics)) okCount++;
            }

            if (writeCover)
            {
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                    var bytes = await http.GetByteArrayAsync(item.HighResCoverUrl);
                    if (bytes.Length > 0)
                    {
                        coverBytes = bytes;
                        okCount++;
                    }
                }
                catch { /* 封面下载失败不阻塞歌词写入 */ }
            }

            if (okCount > 0)
            {
                var edit = new CatClawMusic.Core.Models.AudioTagEdit
                {
                    Lyrics = lyrics,
                    Cover = coverBytes,
                };
                var ok = await _audio.WriteTagsAsync(Song.FilePath, edit);
                if (ok)
                {
                    StatusText = $"已写入 {okCount} 项（{(writeLyrics ? "歌词" : "")}{(writeLyrics && writeCover ? " + " : "")}{(writeCover ? "封面" : "")}）";
                }
                else
                {
                    StatusText = "写入失败（文件不可写？）";
                }
            }
            else
            {
                StatusText = "没有可写入的内容";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"写入失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>统一搜索结果条目：歌词 + 封面合并。</summary>
public class UnifiedSearchResult : ObservableObject
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public int Duration { get; set; }
    public string Source { get; set; } = "";
    public bool HasLyrics { get; set; }
    public string LyricsType { get; set; } = "";
    public bool HasCover { get; set; }
    public string CoverUrl { get; set; } = "";
    public string HighResCoverUrl { get; set; } = "";

    /// <summary>LRCLIB / Lyrico 歌词条目（可能为 null，纯封面结果）</summary>
    public LrclibTrack? LyricsTrack { get; set; }

    /// <summary>iTunes 封面条目（可能为 null，纯歌词结果）</summary>
    public ItunesTrack? CoverTrack { get; set; }

    public string DisplayTitle => $"{Title} - {Artist}";

    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Album)) parts.Add(Album);
            if (Duration > 0) parts.Add(FormatDuration(Duration));
            return string.Join(" · ", parts);
        }
    }

    public string Badge
    {
        get
        {
            var parts = new List<string>();
            if (HasLyrics) parts.Add(LyricsType);
            if (HasCover) parts.Add("封面");
            if (parts.Count == 0) parts.Add(Source);
            return string.Join(" / ", parts);
        }
    }

    public string CoverText
    {
        get
        {
            var t = Title?.Trim() ?? "";
            return t.Length > 0 ? t[..1].ToUpperInvariant() : "♪";
        }
    }

    /// <summary>预览歌词文本（同步优先）</summary>
    public string PreviewLyrics
        => LyricsTrack != null && !string.IsNullOrWhiteSpace(LyricsTrack.SyncedLyrics) ? LyricsTrack.SyncedLyrics!
        : (LyricsTrack != null && !string.IsNullOrWhiteSpace(LyricsTrack.PlainLyrics) ? LyricsTrack.PlainLyrics! : "（该结果无歌词）");

    private static string FormatDuration(int seconds)
    {
        if (seconds <= 0) return "";
        var m = seconds / 60;
        var s = seconds % 60;
        return $"{m}:{s:00}";
    }
}
