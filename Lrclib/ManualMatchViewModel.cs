using System.Collections.ObjectModel;
using CatClawMusic.Plugins.Lrclib.Lyrico;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Storage;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 「歌词匹配」入口页 ViewModel：搜索 LRCLIB 候选 → 选定保存为覆盖记录；
/// 覆盖记录按 歌名|艺人 持久化，之后播放该歌曲时插件优先返回，不再自动匹配。
/// <para>底部「Lyrico 源插件」区：导入 .zip 包安装 JS 源（netease/qq/…），作为 LRCLIB
/// 未命中时的多源歌词兜底；可查看各源加载状态、卸载。</para>
/// </summary>
public partial class ManualMatchViewModel : ObservableObject
{
    private readonly LrclibApiClient _client;
    private readonly OverrideStore _store;
    private readonly LyricoLyricsHub _lyricoHub;
    private readonly IServiceProvider _services;

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

    /// <summary>Lyrico 源管理区状态文本（导入/卸载反馈）</summary>
    [ObservableProperty]
    private string sourceStatusText = string.Empty;

    public ObservableCollection<CandidateItem> Candidates { get; } = new();

    public ObservableCollection<OverrideItem> Overrides { get; } = new();

    /// <summary>已安装的 Lyrico 源插件列表（目录名 + 显示名 + 加载状态）</summary>
    public ObservableCollection<LyricoSourceItem> LyricoSources { get; } = new();

    public ManualMatchViewModel(LrclibApiClient client, OverrideStore store, LyricoLyricsHub lyricoHub, IServiceProvider services)
    {
        _client = client;
        _store = store;
        _lyricoHub = lyricoHub;
        _services = services;
        RefreshOverrides();
        RefreshSources();
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
            StatusText = $"找到 {results.Count} 个候选（可点击下方「使用此歌词」保存）";
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

    /// <summary>把选中的候选保存为覆盖记录（键 = 当前搜索的歌名|艺人）</summary>
    [RelayCommand]
    private void SaveOverride(CandidateItem? item)
    {
        if (item == null) return;

        _store.Set(SearchTitle, SearchArtist, item.Track);
        RefreshOverrides();
        StatusText = $"已保存：{SearchTitle.Trim()} 将优先使用「{item.Track.TrackName} - {item.Track.ArtistName}」的歌词";
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

    // ── Lyrico 源插件管理 ──

    /// <summary>刷新已安装源列表（页面加载/导入/卸载后调用）。</summary>
    public void RefreshSources()
    {
        LyricoSources.Clear();
        try
        {
            foreach (var (dir, name, status) in _lyricoHub.GetSourceInfos())
            {
                var manifest = _lyricoHub.GetManifest(dir);
                var hasConfig = (manifest?.ConfigFields?.Count ?? 0) > 0;
                LyricoSources.Add(new LyricoSourceItem
                {
                    Dir = dir,
                    Name = name,
                    Status = status,
                    HasConfig = hasConfig,
                    IsEnabled = _lyricoHub.IsSourceEnabled(dir),
                });
            }
        }
        catch { }
    }

    /// <summary>导入 Lyrico 源插件 .zip（FilePicker 选文件 → 解包校验 → 装入 LyricoSources → 刷新）。</summary>
    [RelayCommand]
    private async Task ImportLyricoSourceAsync()
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.WinUI] = new[] { ".zip" },
                    [DevicePlatform.Android] = new[] { "application/zip", "application/octet-stream" },
                    [DevicePlatform.iOS] = new[] { "public.zip-archive" },
                    [DevicePlatform.MacCatalyst] = new[] { "public.zip-archive" },
                }),
            }).ConfigureAwait(false);
            if (result == null) return;

            SourceStatusText = "导入中…";
            var r = await LyricoSourceInstaller.ImportAsync(result.FullPath, _lyricoHub);
            if (r.Success)
            {
                RefreshSources();
                SourceStatusText = r.Message + $"（现共 {LyricoSources.Count} 个源）";
            }
            else
            {
                SourceStatusText = r.Message;
            }
        }
        catch (Exception ex)
        {
            SourceStatusText = "导入失败：" + ex.Message;
        }
    }

    /// <summary>卸载 Lyrico 源插件（删除目录 + 刷新内存）。</summary>
    [RelayCommand]
    private void DeleteLyricoSource(LyricoSourceItem? item)
    {
        if (item == null) return;
        var removed = _lyricoHub.DeleteSource(item.Dir);
        if (removed != null)
        {
            RefreshSources();
            SourceStatusText = $"已卸载「{item.Name}」（现共 {LyricoSources.Count} 个源）";
        }
        else
        {
            SourceStatusText = $"卸载失败：{item.Name}";
        }
    }

    /// <summary>打开源插件配置页（仅声明了 configFields 的源可配置）。</summary>
    [RelayCommand]
    private async Task OpenSourceConfigAsync(LyricoSourceItem? item)
    {
        if (item == null) return;
        var page = new Lyrico.LyricoSourceConfigPage(
            new Lyrico.LyricoSourceConfigViewModel(_lyricoHub, item.Dir));
        await PluginNav.PushAsync(page);
    }

    /// <summary>启用/禁用源（不卸载，禁用的源在歌词兜底时跳过）。</summary>
    [RelayCommand]
    private void ToggleSourceEnabled(LyricoSourceItem? item)
    {
        if (item == null) return;
        var enable = !item.IsEnabled;
        _lyricoHub.SetSourceEnabled(item.Dir, enable);
        item.IsEnabled = enable;
        SourceStatusText = enable ? $"已启用「{item.Name}」" : $"已禁用「{item.Name}」";
    }

    /// <summary>打开源测试页（输入歌曲信息验证该源取词）。</summary>
    [RelayCommand]
    private async Task OpenSourceTestAsync(LyricoSourceItem? item)
    {
        if (item == null) return;
        var page = new Lyrico.LyricoSourceTestPage(
            new Lyrico.LyricoSourceTestViewModel(_lyricoHub, item.Dir));
        await PluginNav.PushAsync(page);
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

    /// <summary>歌词形态徽标：同步歌词 / 纯文本 / 无歌词</summary>
    public string Badge => !string.IsNullOrWhiteSpace(Track.SyncedLyrics)
        ? "同步歌词"
        : !string.IsNullOrWhiteSpace(Track.PlainLyrics) ? "纯文本" : "无歌词";

    /// <summary>无歌词的候选不可保存</summary>
    public bool CanSave => !string.IsNullOrWhiteSpace(Track.SyncedLyrics) || !string.IsNullOrWhiteSpace(Track.PlainLyrics);

    /// <summary>无歌词的候选置灰</summary>
    public bool IsUnavailable => !CanSave;

    /// <summary>是否有任何歌词（SearchLyrics 卡片透明度/徽标着色用）。</summary>
    public bool HasLyrics => CanSave;

    /// <summary>预览用歌词文本（同步优先，纯文本兜底）。SearchLyrics 底部预览面板绑定。</summary>
    public string PreviewLyrics
        => !string.IsNullOrWhiteSpace(Track.SyncedLyrics) ? Track.SyncedLyrics!
        : (!string.IsNullOrWhiteSpace(Track.PlainLyrics) ? Track.PlainLyrics! : "");

    /// <summary>封面占位文字（取歌名首字，SearchLyrics/SearchCover 圆形封面占位用）。</summary>
    public string CoverText
    {
        get
        {
            var n = Track.TrackName?.Trim() ?? "";
            return n.Length > 0 ? n[0].ToString() : "♪";
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

/// <summary>已安装的 Lyrico 源插件条目（管理列表展示用）。</summary>
public partial class LyricoSourceItem : ObservableObject
{
    /// <summary>源目录名（manifest.id 或 zip 文件名）</summary>
    public string Dir { get; set; } = string.Empty;

    /// <summary>显示名（manifest.name，缺失用目录名）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>加载状态（已加载/加载失败/未初始化）</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>是否声明了配置项（决定「配置」按钮是否显示）</summary>
    public bool HasConfig { get; set; }

    /// <summary>是否启用（禁用的源在歌词兜底时跳过，按钮文本据此切换）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleText))]
    private bool isEnabled = true;

    public string NameWithStatus => string.IsNullOrEmpty(Status) || Status == "已加载"
        ? Name
        : $"{Name}（{Status}）";

    /// <summary>启停按钮文本（启用→禁用 / 禁用→启用）。</summary>
    public string ToggleText => IsEnabled ? "禁用" : "启用";
}
