using CatClawMusic.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 歌曲信息 ViewModel（Lyrico「Song Info」复刻）：
/// 展示音频文件的技术详情（时长/比特率/采样率/声道/路径/大小/格式）与标签信息，支持一键复制。
/// </summary>
public partial class SongInfoViewModel : ObservableObject
{
    private readonly IAudioFileService? _audio;

    public SongItem Song { get; }

    [ObservableProperty] private bool isLoading = true;
    [ObservableProperty] private string status = "正在读取文件信息...";
    [ObservableProperty] private string title = "";
    [ObservableProperty] private string artist = "";
    [ObservableProperty] private string album = "";
    [ObservableProperty] private string duration = "";
    [ObservableProperty] private string bitrate = "";
    [ObservableProperty] private string sampleRate = "";
    [ObservableProperty] private string channels = "";
    [ObservableProperty] private string filePath = "";
    [ObservableProperty] private string fileSize = "";
    [ObservableProperty] private string format = "";
    [ObservableProperty] private string track = "";
    [ObservableProperty] private string year = "";
    [ObservableProperty] private string genre = "";

    public SongInfoViewModel(SongItem song, IAudioFileService? audio)
    {
        Song = song;
        _audio = audio;
        Title = song.Title;
        Artist = song.Artist;
        Album = song.Song.Album;
    }

    public async Task LoadAsync()
    {
        try
        {
            var tag = _audio is null ? null : await _audio.ReadTagsAsync(Song.FilePath);
            if (tag == null)
            {
                // 标签不可读时退回宿主模型里已有的基础信息
                FillFromSong();
                Status = "无法读取文件（可能已删除或不受支持）";
            }
            else
            {
                Title = !string.IsNullOrWhiteSpace(tag.Title) ? tag.Title : Song.Title;
                Artist = !string.IsNullOrWhiteSpace(tag.Artist) ? tag.Artist : Song.Artist;
                Album = tag.Album ?? "";
                Track = tag.TrackNumber ?? "";
                Year = tag.Year ?? "";
                Genre = tag.Genre ?? "";
                Duration = FormatDurationMs(tag.DurationMs > 0 ? tag.DurationMs : Song.Song.Duration * 1000L);
                Bitrate = tag.Bitrate > 0 ? $"{tag.Bitrate} kbps" : "";
                SampleRate = tag.SampleRate > 0 ? $"{tag.SampleRate / 1000.0:0.#} kHz" : "";
                Channels = tag.Channels > 0 ? $"{tag.Channels} 声道" : "";
                FilePath = tag.FilePath ?? Song.FilePath;
                FileSize = FormatBytes(tag.FileSize > 0 ? tag.FileSize : Song.Song.FileSize);
                Format = tag.Extension.TrimStart('.').ToUpperInvariant();
                Status = "已读取文件信息";
            }
        }
        catch (Exception ex)
        {
            FillFromSong();
            Status = $"读取失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void FillFromSong()
    {
        Duration = ThemeHelper.FormatDuration(NormalizeDurationSeconds(Song.Song.Duration));
        Bitrate = Song.Song.Bitrate > 0 ? $"{Song.Song.Bitrate} kbps" : "";
        FilePath = Song.FilePath;
        FileSize = FormatBytes(Song.Song.FileSize);
        Format = System.IO.Path.GetExtension(Song.FilePath).TrimStart('.').ToUpperInvariant();
        Track = Song.Song.TrackNumber > 0 ? Song.Song.TrackNumber.ToString() : "";
        Year = Song.Song.Year > 0 ? Song.Song.Year.ToString() : "";
        Genre = Song.Song.Genre ?? "";
    }

    /// <summary>生成可复制/分享的纯文本信息</summary>
    public string BuildInfoText()
    {
        var lines = new (string k, string v)[]
        {
            ("标题", Title), ("艺人", Artist), ("专辑", Album),
            ("音轨", Track), ("年份", Year), ("流派", Genre),
            ("时长", Duration), ("比特率", Bitrate), ("采样率", SampleRate),
            ("声道", Channels), ("文件路径", FilePath), ("文件大小", FileSize),
            ("格式", Format),
        };
        return string.Join("\n", lines.Where(x => !string.IsNullOrWhiteSpace(x.v)).Select(x => $"{x.k}：{x.v}"));
    }

    private static int NormalizeDurationSeconds(int duration)
        => duration > 1000 ? duration / 1000 : duration;

    private static string FormatDurationMs(long ms)
    {
        if (ms <= 0) return "";
        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
            : $"{ts.Minutes}:{ts.Seconds:00}";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "未知";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }
}