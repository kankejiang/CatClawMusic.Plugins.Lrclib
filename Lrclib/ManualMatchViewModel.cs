using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 「歌词匹配」入口页 ViewModel：搜索 LRCLIB 候选 → 底部面板预览歌词 → 选定保存为覆盖记录；
/// 覆盖记录按 歌名|艺人 持久化，之后播放该歌曲时插件优先返回，不再自动匹配。
/// </summary>
public partial class ManualMatchViewModel : ObservableObject
{
    private readonly LrclibApiClient _client;
    private readonly OverrideStore _store;

    /// <summary>候选搜索关键词：歌名（必须与歌曲标签一致，否则覆盖不生效）</summary>
    [ObservableProperty]
    private string searchTitle = string.Empty;

    /// <summary>候选搜索关键词：艺人（可空）</summary>
    [ObservableProperty]
    private string searchArtist = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusText = string.Empty;

    /// <summary>当前标签：0 = 候选，1 = 已保存（切换展示列表）</summary>
    [ObservableProperty]
    private int activeTab;

    /// <summary>底部面板当前选中的候选（预览歌词 / 保存覆盖）</summary>
    [ObservableProperty]
    private CandidateItem? selectedCandidate;

    /// <summary>是否显示候选底部面板</summary>
    [ObservableProperty]
    private bool showCandidateSheet;

    public ObservableCollection<CandidateItem> Candidates { get; } = new();

    public ObservableCollection<OverrideItem> Overrides { get; } = new();

    public ManualMatchViewModel(LrclibApiClient client, OverrideStore store)
    {
        _client = client;
        _store = store;
        RefreshOverrides();
    }

    /// <summary>搜索 LRCLIB 候选（按歌名+艺人，最多取前 50 条）</summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchTitle)) return;

        IsBusy = true;
        StatusText = string.Empty;
        Candidates.Clear();
        try
        {
            var artist = string.IsNullOrWhiteSpace(SearchArtist) ? null : SearchArtist.Trim();
            var results = await _client.SearchAsync(SearchTitle.Trim(), artist);
            if (results == null || results.Count == 0)
            {
                StatusText = "没有找到候选（检查歌名/艺人拼写，或该歌在 LRCLIB 未被收录）";
                return;
            }
            foreach (var t in results.Take(50))
                Candidates.Add(new CandidateItem(t));
            // 命中后自动切到「候选」标签
            var title = SearchTitle.Trim();
            StatusText = $"找到 {results.Count} 个候选 · {title.Trim()} — 点卡片预览/使用歌词";
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

    /// <summary>点击候选：打开底部面板预览歌词</summary>
    [RelayCommand]
    private void OpenCandidate(CandidateItem? item)
    {
        if (item == null) return;
        if (!item.HasLyrics)
        {
            StatusText = "该候选无歌词，无法保存";
            return;
        }
        SelectedCandidate = item;
        ShowCandidateSheet = true;
    }

    /// <summary>关闭候选底部面板</summary>
    [RelayCommand]
    private void CloseSheet() => ShowCandidateSheet = false;

    /// <summary>底部面板「使用此歌词」：把当前选中候选保存为覆盖记录（键 = 当前搜索的歌名|艺人）</summary>
    [RelayCommand]
    private void ApplySelected()
    {
        var candidate = SelectedCandidate;
        if (candidate == null) return;
        var trackName = candidate.Track.TrackName?.Trim();
        _store.Set(SearchTitle, SearchArtist, candidate.Track);
        ShowCandidateSheet = false;
        SelectedCandidate = null;
        RefreshOverrides();
        StatusText = $"已保存：{SearchTitle.Trim()} 将优先使用「{trackName}」的歌词";
    }

    /// <summary>删除覆盖记录，恢复自动匹配</summary>
    [RelayCommand]
    private void RemoveOverride(OverrideItem? item)
    {
        if (item == null) return;

        _store.RemoveKey(item.Key);
        RefreshOverrides();
        StatusText = $"已删除覆盖：{item.Key}（恢复自动匹配）";
    }

    /// <summary>刷新覆盖记录列表（页面加载/增删后调用）</summary>
    public void RefreshOverrides()
    {
        Overrides.Clear();
        foreach (var (key, title, artist, track) in _store.GetAll())
        {
            Overrides.Add(new OverrideItem
            {
                Key = key,
                Display = $"{title} / {artist}",
                Subtitle = $"{track.TrackName} - {track.ArtistName}（{FormatDuration(track.Duration)}）",
            });
        }
    }

    internal static string FormatDuration(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }
}

/// <summary>LRCLIB 候选条目（列表展示用）</summary>
public class CandidateItem
{
    public LrclibTrack Track { get; }

    public CandidateItem(LrclibTrack track) => Track = track;

    public string DisplayTitle => $"{Track.TrackName} - {Track.ArtistName}";

    public string Subtitle
    {
        get
        {
            var album = string.IsNullOrWhiteSpace(Track.AlbumName) ? "未知专辑" : Track.AlbumName;
            return $"{album} · {ManualMatchViewModel.FormatDuration(Track.Duration)}";
        }
    }

    /// <summary>是否有歌词（同步或纯文本），决定能否保存</summary>
    public bool HasLyrics =>
        !string.IsNullOrWhiteSpace(Track.SyncedLyrics) || !string.IsNullOrWhiteSpace(Track.PlainLyrics);

    /// <summary>歌词形态徽标：同步歌词 / 纯文本 / 无歌词</summary>
    public string Badge => !string.IsNullOrWhiteSpace(Track.SyncedLyrics)
        ? "同步歌词"
        : !string.IsNullOrWhiteSpace(Track.PlainLyrics) ? "纯文本" : "无歌词";

    /// <summary>无歌词的候选不可保存（置灰）</summary>
    public bool IsUnavailable => !HasLyrics;

    /// <summary>封面占位：取歌名首字符，作为方形色块的文字</summary>
    public string CoverText
    {
        get
        {
            var t = string.IsNullOrWhiteSpace(Track.TrackName) ? "♪" : Track.TrackName.Trim();
            return t.Length > 0 ? t[..1].ToUpperInvariant() : "♪";
        }
    }

    /// <summary>底部面板歌词预览文本（同步优先，纯文本兜底），无则为空</summary>
    public string PreviewLyrics
    {
        get
        {
            var synced = Track.SyncedLyrics;
            if (!string.IsNullOrWhiteSpace(synced)) return synced;
            return Track.PlainLyrics ?? string.Empty;
        }
    }
}

/// <summary>覆盖记录条目（管理列表展示用）</summary>
public class OverrideItem
{
    /// <summary>规范化键 歌名|艺人（与插件查找一致）</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>歌曲显示名（来自键）</summary>
    public string Display { get; set; } = string.Empty;

    /// <summary>当前选定的 LRCLIB 曲目</summary>
    public string Subtitle { get; set; } = string.Empty;
}