using CatClawMusic.Core.Interfaces;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 封面搜索补全 ViewModel（Lyrico SearchCover 复刻）：
/// 用 iTunes Search API（免费无 Key）按歌名/艺人搜索候选 → 预览 → 「写入标签」把封面写进音频文件。
/// </summary>
public partial class SearchCoverViewModel : ObservableObject
{
    private readonly ItunesApiClient _client;
    private readonly IAudioFileService? _audio;

    public SongItem Song { get; }

    [ObservableProperty] private string searchTitle = "";
    [ObservableProperty] private string searchArtist = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private bool showPreview;
    [ObservableProperty] private CoverCandidate? selected;

    public ObservableCollection<CoverCandidate> Candidates { get; } = new();

    public SearchCoverViewModel(SongItem song, ItunesApiClient client, IAudioFileService? audio)
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
                StatusText = "未找到候选（iTunes 未收录或歌名不匹配）";
                return;
            }
            foreach (var t in results.Take(50))
                Candidates.Add(new CoverCandidate(t));
            StatusText = $"找到 {results.Count} 个候选，点卡片预览封面";
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
    private void OpenCandidate(CoverCandidate? item)
    {
        if (item == null || !item.HasCover) return;
        Selected = item;
        ShowPreview = true;
    }

    [RelayCommand]
    private void ClosePreview() => ShowPreview = false;

    /// <summary>下载选中候选的封面图并写入当前歌曲文件的内嵌封面标签</summary>
    [RelayCommand]
    private async Task WriteCoverAsync()
    {
        var candidate = Selected;
        if (candidate == null || !candidate.HasCover) return;
        if (_audio is null || string.IsNullOrWhiteSpace(Song.FilePath))
        {
            StatusText = "宿主未提供写文件服务或歌曲无本地路径";
            return;
        }

        try
        {
            StatusText = "正在下载封面...";
            var bytes = await _client.DownloadCoverAsync(candidate.HighResUrl);
            if (bytes == null || bytes.Length == 0)
            {
                StatusText = "封面下载失败";
                return;
            }

            var ok = await _audio.WriteTagsAsync(Song.FilePath, new CatClawMusic.Core.Models.AudioTagEdit
            {
                Cover = bytes,
            });
            StatusText = ok
                ? $"已写入封面：{candidate.Track.TrackName}"
                : "写入失败（文件不可写？）";
            if (ok) ShowPreview = false;
        }
        catch
        {
            StatusText = "封面写入失败";
        }
    }
}

/// <summary>iTunes 封面候选条目（列表展示用）</summary>
public class CoverCandidate
{
    public ItunesTrack Track { get; }

    public CoverCandidate(ItunesTrack track) => Track = track;

    public string DisplayTitle => $"{Track.TrackName} - {Track.ArtistName}";

    public string Subtitle => string.IsNullOrWhiteSpace(Track.CollectionName)
        ? "未知专辑"
        : Track.CollectionName;

    /// <summary>是否有封面图</summary>
    public bool HasCover => !string.IsNullOrWhiteSpace(Track.ArtworkUrl100);

    /// <summary>封面占位：取歌名首字符</summary>
    public string CoverText
    {
        get
        {
            var t = string.IsNullOrWhiteSpace(Track.TrackName) ? "♪" : Track.TrackName.Trim();
            return t.Length > 0 ? t[..1].ToUpperInvariant() : "♪";
        }
    }

    /// <summary>预览用缩略图地址（100px）</summary>
    public string ThumbUrl => Track.ArtworkUrl100 ?? string.Empty;

    /// <summary>写入用高清图地址（把 100x100 换成 600x600）</summary>
    public string HighResUrl
    {
        get
        {
            var url = Track.ArtworkUrl100;
            if (string.IsNullOrWhiteSpace(url)) return url ?? string.Empty;
            return url.Replace("100x100bb", "600x600bb");
        }
    }
}

/// <summary>
/// iTunes Search API 客户端（https://itunes.apple.com/search）。
/// 免费、无需 API Key，返回 Apple Music / iTunes 商店歌曲条目及封面图 URL。
/// </summary>
public class ItunesApiClient
{
    private const string SearchUrl = "https://itunes.apple.com/search";

    private readonly HttpClient _http;

    public ItunesApiClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CatClawMusic/1.0 (cover provider plugin)");
    }

    /// <summary>按歌名/艺人搜索歌曲条目</summary>
    public async Task<List<ItunesTrack>?> SearchAsync(string trackName, string? artistName = null)
    {
        var term = artistName is { Length: > 0 }
            ? $"{trackName} {artistName}"
            : trackName;
        var url = $"{SearchUrl}?entity=song&limit=50&term={Uri.EscapeDataString(term)}";

        try
        {
            using var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            var text = await resp.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                return null;
            return JsonSerializer.Deserialize<List<ItunesTrack>>(results.GetRawText());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>下载封面图字节（JPEG）</summary>
    public async Task<byte[]?> DownloadCoverAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            using var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsByteArrayAsync();
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>iTunes 搜索条目（只保留插件需要的字段）</summary>
public class ItunesTrack
{
    [JsonPropertyName("trackName")]
    public string TrackName { get; set; } = string.Empty;

    [JsonPropertyName("artistName")]
    public string ArtistName { get; set; } = string.Empty;

    [JsonPropertyName("collectionName")]
    public string? CollectionName { get; set; }

    [JsonPropertyName("artworkUrl100")]
    public string? ArtworkUrl100 { get; set; }
}
