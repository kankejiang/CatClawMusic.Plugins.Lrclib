using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Plugins.Lrclib.Lyrico;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Text;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 歌词搜索补全 ViewModel（Lyrico SearchLyrics 复刻）：
/// 按歌名/艺人搜 LRCLIB 候选列表 + 底部预览 + 「写入标签」把歌词写进音频文件内嵌歌词。
/// LRCLIB 之外同时并行搜索已导入的 Lyrico 多源（netease/qq/kugou/soda/apple…），
/// 每个命中源以「源名」徽标追加进候选列表，与 Lyrico 多源同显。
/// </summary>
public partial class SearchLyricsViewModel : ObservableObject
{
    private readonly LrclibApiClient _client;
    private readonly IAudioFileService? _audio;
    private readonly LyricoLyricsHub? _lyricoHub;

    public SongItem Song { get; }

    [ObservableProperty] private string searchTitle = "";
    [ObservableProperty] private string searchArtist = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private bool showPreview;
    [ObservableProperty] private CandidateItem? selected;

    // ── 写入前处理选项（复刻 Lyrico LyricRenderConfig） ──
    [ObservableProperty] private LyricConversionMode conversionMode;
    [ObservableProperty] private int offsetSeconds;
    [ObservableProperty] private bool removeEmptyLines = true;

    /// <summary>简繁转换 Picker 下标（0/1/2 → None/繁→简/简→繁）</summary>
    public int ConversionModeIndex
    {
        get => (int)conversionMode;
        set => ConversionMode = (LyricConversionMode)value;
    }

    public ObservableCollection<CandidateItem> Candidates { get; } = new();

    public SearchLyricsViewModel(SongItem song, LrclibApiClient client, IAudioFileService? audio,
        LyricoLyricsHub? lyricoHub = null)
    {
        Song = song;
        _client = client;
        _audio = audio;
        _lyricoHub = lyricoHub;
        SearchTitle = song.Title;
        SearchArtist = song.Artist;
    }

    /// <summary>搜索世代号：防止上一轮 Lyrico 扇出的迟到结果混入新一轮候选。</summary>
    private int _searchGeneration;

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchTitle)) return;

        IsBusy = true;
        StatusText = "搜索中...";
        Candidates.Clear();
        var generation = ++_searchGeneration;
        var title = SearchTitle.Trim();
        var artist = string.IsNullOrWhiteSpace(SearchArtist) ? null : SearchArtist.Trim();
        var lrclibFound = 0;
        try
        {
            var results = await _client.SearchAsync(title, artist);
            if (results != null)
            {
                lrclibFound = results.Count;
                foreach (var t in results.Take(50))
                    Candidates.Add(new CandidateItem(t));
                if (results.Count > 0)
                    StatusText = $"LRCLIB：{results.Count} 个候选 · Lyrico 源匹配中…";
            }
        }
        catch
        {
            StatusText = "LRCLIB 搜索失败（网络不可用？）";
        }
        finally
        {
            IsBusy = false;
        }

        if (lrclibFound == 0)
            StatusText = "LRCLIB 未收录，正在搜索 Lyrico 多源…";

        // 同时搜索已导入的 Lyrico 多源（netease/qq/kugou/soda/apple…），命中以源名徽标逐个追加
        _ = SearchLyricoSourcesAsync(generation, title, artist);
    }

    /// <summary>并行搜索全部启用的 Lyrico 源，命中者以源名徽标追加进候选列表（与 LRCLIB 同显）。</summary>
    private async Task SearchLyricoSourcesAsync(int generation, string title, string? artist)
    {
        var hub = _lyricoHub;
        if (hub == null) return;

        // 宿主 Duration 存在秒/毫秒单位不一致，沿用 >1000 视为毫秒的防御判断
        var duration = Song.Song.Duration > 1000 ? Song.Song.Duration / 1000.0 : Song.Song.Duration;
        try
        {
            var hits = await hub.SearchAllSourcesAsync(title, artist, null, duration);
            if (generation != _searchGeneration) return;   // 期间用户又发起了新搜索，丢弃本轮

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (generation != _searchGeneration) return;
                foreach (var (dir, name, lyrics) in hits)
                {
                    var item = ToCandidate(name, lyrics, title, artist);
                    if (item != null) Candidates.Add(item);
                }
                if (hits.Count > 0)
                    StatusText = $"共 {Candidates.Count} 个候选（Lyrico 命中 {hits.Count} 个源），点卡片预览歌词";
                else if (Candidates.Count == 0)
                    StatusText = "LRCLIB 与 Lyrico 源均未找到候选";
            });
        }
        catch { }
    }

    /// <summary>Lyrico 源歌词 → 候选项（渲染为带时间戳 LRC 文本，复用预览/写标签管线）。</summary>
    private static CandidateItem? ToCandidate(string sourceName, LrcLyrics lyrics, string title, string? artist)
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

        var track = new LrclibTrack
        {
            TrackName = string.IsNullOrWhiteSpace(lyrics.Metadata?.Title) ? title : lyrics.Metadata!.Title,
            ArtistName = string.IsNullOrWhiteSpace(lyrics.Metadata?.Artist) ? (artist ?? "") : lyrics.Metadata!.Artist,
            AlbumName = lyrics.Metadata?.Album,
            SyncedLyrics = sb.ToString(),
        };
        return new CandidateItem(track) { SourceTag = sourceName };
    }

    [RelayCommand]
    private void OpenCandidate(CandidateItem? item)
    {
        if (item == null) return;
        if (!item.HasLyrics)
        {
            StatusText = "该候选无歌词，无法写入";
            return;
        }
        Selected = item;
        ShowPreview = true;
    }

    [RelayCommand]
    private void ClosePreview() => ShowPreview = false;

    /// <summary>把选中候选的歌词写入当前歌曲文件的内嵌歌词标签</summary>
    [RelayCommand]
    private async Task WriteLyricsAsync()
    {
        var candidate = Selected;
        if (candidate == null || !candidate.HasLyrics) return;
        if (_audio is null || string.IsNullOrWhiteSpace(Song.FilePath))
        {
            StatusText = "宿主未提供写文件服务或歌曲无本地路径";
            return;
        }

        var processed = ProcessLyrics(candidate.PreviewLyrics);
        var ok = await _audio.WriteTagsAsync(Song.FilePath, new CatClawMusic.Core.Models.AudioTagEdit
        {
            Lyrics = processed,
        });
        StatusText = ok
            ? $"已写入内嵌歌词：{candidate.Track.TrackName}"
            : "写入失败（文件不可写？）";
        if (ok) ShowPreview = false;
    }

    /// <summary>
    /// 按当前处理选项生成歌词：清洗（去空行/占位行/标签行）→ 简繁转换 → 时间轴偏移。
    /// 仅清洗无效内容后为空时回退原始文本。
    /// </summary>
    private string ProcessLyrics(string raw)
    {
        var tagKeywords = new LyricCleanupRulesStore().GetTagKeywords();
        var cleaned = LyricProcessor.Cleanup(raw, RemoveEmptyLines, tagKeywords);
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = raw;
        var converted = LyricProcessor.ConvertLyrics(cleaned, ConversionMode);
        return LyricProcessor.ShiftOffset(converted, OffsetSeconds * 1000);
    }
}
