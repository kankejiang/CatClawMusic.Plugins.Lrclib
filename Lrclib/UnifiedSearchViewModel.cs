using System.Collections.ObjectModel;
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

    /// <summary>写入成功后触发（页面据此关闭抽屉）。</summary>
    public event EventHandler? Applied;

    [ObservableProperty] private string searchTitle = "";
    [ObservableProperty] private string searchArtist = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusText = string.Empty;
    [ObservableProperty] private bool showPreview;
    [ObservableProperty] private UnifiedSearchResult? selected;

    /// <summary>写入开关：默认全选。分别控制是否写入歌词 / 封面 / 元数据（歌名艺人专辑）。</summary>
    [ObservableProperty] private bool applyLyrics = true;
    [ObservableProperty] private bool applyCover = true;
    [ObservableProperty] private bool applyMetadata = true;

    /// <summary>当前选中的歌词模式（预览与写入共用）。</summary>
    [ObservableProperty]
    private LyricMode selectedMode = LyricMode.Plain;

    partial void OnSelectedModeChanged(LyricMode value)
        => OnPropertyChanged(nameof(SelectedLyricsPreview));

    /// <summary>按当前模式渲染选中结果的歌词文本（预览用）。</summary>
    public string SelectedLyricsPreview
        => Selected != null ? LyricModeEncoder.Encode(Selected.StructuredLyrics, SelectedMode) : "（未选中）";

    partial void OnSelectedChanged(UnifiedSearchResult? value)
        => OnPropertyChanged(nameof(SelectedLyricsPreview));

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

        var lyricoTask = _lyricoHub != null ? Task.Run<List<(LrclibTrack track, string source, LrcLyrics structured)>>(async () =>
        {
            try
            {
                var duration = Song.Song.Duration > 1000 ? Song.Song.Duration / 1000.0 : Song.Song.Duration;
                var hits = await _lyricoHub.SearchAllSourcesAsync(title, artist, null, duration);
                var result = new List<(LrclibTrack, string, LrcLyrics)>();
                foreach (var entry in hits)
                {
                    var t = LyricoToLrclibTrack(entry.Name, entry.Lyrics, title, artist);
                    if (t != null) result.Add((t, entry.Name, entry.Lyrics));
                }
                return result;
            }
            catch { return new List<(LrclibTrack, string, LrcLyrics)>(); }
        }) : Task.FromResult(new List<(LrclibTrack, string, LrcLyrics)>());

        await Task.WhenAll(lrclibTask, itunesTask, lyricoTask);

        var lrclibResults = lrclibTask.Result ?? new List<LrclibTrack>();
        var itunesResults = itunesTask.Result ?? new List<ItunesTrack>();
        var lyricoResults = lyricoTask.Result ?? new List<(LrclibTrack track, string source, LrcLyrics structured)>();

        // 合并歌词候选
        var allLyrics = new List<(LrclibTrack track, string source, LrcLyrics? structured)>();
        foreach (var t in lrclibResults.Take(30))
        {
            var structured = LrcFromSyncedLyrics(t.SyncedLyrics);
            allLyrics.Add((t, "LRCLIB", structured));
        }
        foreach (var (t, src, structured) in lyricoResults.Take(30))
            allLyrics.Add((t, src, structured));

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

        foreach (var (track, source, structured) in allLyrics)
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
                StructuredLyrics = structured,
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

        _all = merged.Take(50).ToList();
        RebuildFilters();

        IsBusy = false;
        var total = _all.Count;
        var withLyrics = _all.Count(r => r.HasLyrics);
        var withCover = _all.Count(r => r.HasCover);
        StatusText = total == 0
            ? "没有找到结果（检查关键字，或该歌未被收录）"
            : $"找到 {total} 个结果（歌词 {withLyrics} / 封面 {withCover}）";
    }

    // ── 来源筛选 ──

    private List<UnifiedSearchResult> _all = new();
    private string _selectedSource = "";

    /// <summary>筛选 chip 集合（第一位固定「全部」），动态来自当前结果的不同来源。</summary>
    public ObservableCollection<SourceFilter> SourceFilters { get; } = new();

    /// <summary>按所选来源过滤后的结果（绑定列表用）。</summary>
    public ObservableCollection<UnifiedSearchResult> FilteredResults { get; } = new();

    private void RebuildFilters()
    {
        var labels = _all
            .Select(r => SourceLabel(r.Source))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToList();

        SourceFilters.Clear();
        SourceFilters.Add(new SourceFilter { Label = "全部", IsActive = true });

        foreach (var l in labels)
            SourceFilters.Add(new SourceFilter { Label = l });

        _selectedSource = "";
        ApplyFilter();
    }

    [RelayCommand]
    private void SelectSource(SourceFilter? f)
    {
        if (f == null) return;
        foreach (var s in SourceFilters)
            s.IsActive = ReferenceEquals(s, f);
        _selectedSource = f.Label;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredResults.Clear();
        foreach (var r in _all)
        {
            if (string.IsNullOrEmpty(_selectedSource) || _selectedSource == "全部")
                FilteredResults.Add(r);
            else if (string.Equals(SourceLabel(r.Source), _selectedSource, StringComparison.OrdinalIgnoreCase))
                FilteredResults.Add(r);
        }
    }

    /// <summary>把内部来源标识映射为筛选/展示名：iTunes → 苹果，其余原样（LRCLIB / Lyrico 源显示名）。</summary>
    internal static string SourceLabel(string raw)
        => string.Equals(raw, "iTunes", StringComparison.OrdinalIgnoreCase) ? "苹果" : (raw ?? "").Trim();

    /// <summary>把 SyncedLyrics（标准/增强/逐字 LRC，含同时间戳多语言行）解析成结构化 LrcLyrics；无法解析返回 null。</summary>
    private static LrcLyrics? LrcFromSyncedLyrics(string? synced)
        => LrcUnifiedParser.Parse(synced);

    /// <summary>Lyrico LrcLyrics → LrclibTrack（按逐行模式编码，保留词级/翻译/罗马音数据，复用结果展示/写入管线）。</summary>
    private static LrclibTrack? LyricoToLrclibTrack(string sourceName, LrcLyrics lyrics, string title, string? artist)
    {
        if (lyrics.Lines.Count == 0) return null;
        var synced = LyricModeEncoder.Encode(lyrics, LyricMode.Plain);
        if (string.IsNullOrWhiteSpace(synced)) return null;

        return new LrclibTrack
        {
            TrackName = string.IsNullOrWhiteSpace(lyrics.Metadata?.Title) ? title : lyrics.Metadata!.Title,
            ArtistName = string.IsNullOrWhiteSpace(lyrics.Metadata?.Artist) ? (artist ?? "") : lyrics.Metadata!.Artist,
            AlbumName = lyrics.Metadata?.Album,
            SyncedLyrics = synced,
        };
    }

    /// <summary>点击结果直接写入歌词 + 封面到文件。</summary>
    [RelayCommand]
    private void OpenPreview(UnifiedSearchResult? item)
    {
        if (item == null) return;
        Selected = item;
        ShowPreview = true;
    }

    [RelayCommand]
    private void ClosePreview() => ShowPreview = false;

    /// <summary>按勾选写入选中结果的元数据 / 歌词 / 封面到文件。</summary>
    [RelayCommand]
    private async Task ApplyAsync()
    {
        var item = Selected;
        if (item == null || _audio == null) return;

        var writeLyrics = ApplyLyrics && item.HasLyrics && item.LyricsTrack != null;
        var writeCover = ApplyCover && item.HasCover && !string.IsNullOrWhiteSpace(item.HighResCoverUrl);
        var writeMetadata = ApplyMetadata;

        if (!writeLyrics && !writeCover && !writeMetadata)
        {
            StatusText = "请至少勾选一项要写入的内容";
            return;
        }

        IsBusy = true;
        var parts = new List<string>();
        try
        {
            string? lyrics = null;
            byte[]? coverBytes = null;

            if (writeLyrics && item.LyricsTrack != null)
            {
                // 优先按所选歌词模式从结构化歌词编码；无结构化才回退原字符串。
                lyrics = item.StructuredLyrics != null
                    ? LyricModeEncoder.Encode(item.StructuredLyrics, SelectedMode)
                    : (!string.IsNullOrWhiteSpace(item.LyricsTrack.SyncedLyrics)
                        ? item.LyricsTrack.SyncedLyrics
                        : item.LyricsTrack.PlainLyrics);
                var part = SelectedMode == LyricMode.Plain ? "歌词" : $"{LyricModeEncoder.ModeName(SelectedMode)}";
                if (!string.IsNullOrWhiteSpace(lyrics)) parts.Add(part);
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
                        parts.Add("封面");
                    }
                }
                catch { /* 封面下载失败不阻塞其它写入 */ }
            }

            // 元数据：用搜索结果的歌名/艺人/专辑
            if (writeMetadata) parts.Add("元数据");

            if (parts.Count > 0)
            {
                var edit = new CatClawMusic.Core.Models.AudioTagEdit
                {
                    Lyrics = lyrics,
                    Cover = coverBytes,
                };
                if (writeMetadata)
                {
                    if (!string.IsNullOrWhiteSpace(item.Title)) edit.Title = item.Title;
                    if (!string.IsNullOrWhiteSpace(item.Artist)) edit.Artist = item.Artist;
                    if (!string.IsNullOrWhiteSpace(item.Album)) edit.Album = item.Album;
                }

                var ok = await _audio.WriteTagsAsync(Song.FilePath, edit);
                if (ok)
                {
                    StatusText = $"已写入：{string.Join(" + ", parts)}";
                    Applied?.Invoke(this, EventArgs.Empty);
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

/// <summary>来源筛选 chip：Label = 显示名，IsActive = 是否选中。</summary>
public partial class SourceFilter : ObservableObject
{
    public string Label { get; set; } = "";

    [ObservableProperty]
    private bool isActive;
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

    /// <summary>结构化歌词（含词级时间戳），用于四种歌词模式预览/写入。可能为 null。</summary>
    public LrcLyrics? StructuredLyrics { get; set; }

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
