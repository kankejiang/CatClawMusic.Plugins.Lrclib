using CatClawMusic.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 歌词搜索补全 ViewModel（Lyrico SearchLyrics 复刻）：
/// 按歌名/艺人搜 LRCLIB → 候选列表 → 底部预览 → 「写入标签」把歌词写进音频文件内嵌歌词。
/// </summary>
public partial class SearchLyricsViewModel : ObservableObject
{
    private readonly LrclibApiClient _client;
    private readonly IAudioFileService? _audio;

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

    public SearchLyricsViewModel(SongItem song, LrclibApiClient client, IAudioFileService? audio)
    {
        Song = song;
        _client = client;
        _audio = audio;
        SearchTitle = song.Title;
        SearchArtist = song.Artist;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchTitle)) return;

        IsBusy = true;
        StatusText = "搜索中...";
        Candidates.Clear();
        try
        {
            var artist = string.IsNullOrWhiteSpace(SearchArtist) ? null : SearchArtist.Trim();
            var results = await _client.SearchAsync(SearchTitle.Trim(), artist);
            if (results == null || results.Count == 0)
            {
                StatusText = "未找到候选（LRCLIB 未收录或歌名不匹配）";
                return;
            }
            foreach (var t in results.Take(50))
                Candidates.Add(new CandidateItem(t));
            StatusText = $"找到 {results.Count} 个候选，点卡片预览歌词";
        }
        catch
        {
            StatusText = "搜索失败（网络不可用？）";
        }
        finally
        {
            IsBusy = false;
        }
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
