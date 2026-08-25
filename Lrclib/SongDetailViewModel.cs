using CatClawMusic.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 歌曲详情 ViewModel：经宿主 <see cref="IAudioFileService"/> 读取该歌曲当前标签，
/// 供详情页展示并可跳转编辑 / 搜索补全。
/// </summary>
public partial class SongDetailViewModel : ObservableObject
{
    private readonly IAudioFileService? _audio;

    public SongItem Song { get; }

    [ObservableProperty] private bool isLoading = true;
    [ObservableProperty] private string tagStatus = "正在读取标签...";
    [ObservableProperty] private string tagTitle = "";
    [ObservableProperty] private string tagArtist = "";
    [ObservableProperty] private string tagAlbum = "";
    [ObservableProperty] private string tagYear = "";
    [ObservableProperty] private string tagGenre = "";
    [ObservableProperty] private string tagTrack = "";
    [ObservableProperty] private string tagLyricsPreview = "";
    [ObservableProperty] private string fileInfo = "";
    [ObservableProperty] private bool hasLyrics;
    [ObservableProperty] private ImageSource? coverSource;

    public SongDetailViewModel(SongItem song, IAudioFileService? audio)
    {
        Song = song;
        _audio = audio;
        tagTitle = song.Title;
        tagArtist = song.Artist;
        tagAlbum = song.Song.Album;
    }

    public async Task LoadAsync()
    {
        if (string.IsNullOrWhiteSpace(Song.FilePath))
        {
            TagStatus = "无本地文件路径";
            IsLoading = false;
            return;
        }

        try
        {
            var tag = _audio is null ? null : await _audio.ReadTagsAsync(Song.FilePath);
            if (tag == null)
            {
                TagStatus = "未能读取标签（文件不存在或不受支持）";
                FileInfo = FormatBytes(Song.Song.FileSize);
            }
            else
            {
                TagTitle = !string.IsNullOrWhiteSpace(tag.Title) ? tag.Title : Song.Title;
                TagArtist = !string.IsNullOrWhiteSpace(tag.Artist) ? tag.Artist : Song.Artist;
                TagAlbum = tag.Album ?? "";
                TagYear = tag.Year ?? "";
                TagGenre = tag.Genre ?? "";
                TagTrack = tag.TrackNumber ?? "";
                TagLyricsPreview = tag.Lyrics is { Length: > 0 } ? tag.Lyrics : "";
                HasLyrics = TagLyricsPreview.Length > 0;
                CoverSource = tag.Cover is { Length: > 0 } ? ImageSource.FromStream(() => new MemoryStream(tag.Cover)) : null;
                FileInfo = $"{tag.DisplayName} · {FormatBytes(tag.FileSize)} · {tag.Extension.TrimStart('.')}";
                TagStatus = "已从文件读取标签";
            }
        }
        catch (Exception ex)
        {
            TagStatus = $"读取标签失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "未知大小";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }
}
