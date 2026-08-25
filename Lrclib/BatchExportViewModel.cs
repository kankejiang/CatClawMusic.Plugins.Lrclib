using System.Collections.ObjectModel;
using System.IO;
using CatClawMusic.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 批量导出 ViewModel（Lyrico <c>BatchExportProcessor</c> 复刻）：
/// 多选歌曲 → 填目标文件夹路径 → 导出内嵌歌词（.lrc）和/或封面（.jpg）到文件夹。
/// <para>桌面端用 File I/O 写文件；目标路径不存在或不可写（如 Android SAF）时提示。</para>
/// </summary>
public partial class BatchExportViewModel : ObservableObject
{
    private readonly IReadOnlyList<SongItem> _songs;
    private readonly IAudioFileService? _audio;

    [ObservableProperty] private bool exportLyrics = true;
    [ObservableProperty] private bool exportCover;
    [ObservableProperty] private string folderPath = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private int totalSongs;
    [ObservableProperty] private int doneCount;

    public ObservableCollection<ExportResultItem> Results { get; } = new();

    public BatchExportViewModel(IReadOnlyList<SongItem> songs, IAudioFileService? audio)
    {
        _songs = songs;
        _audio = audio;
        TotalSongs = songs.Count;
    }

    /// <summary>执行导出：逐首读标签 → 写 .lrc/.jpg 到目标文件夹。</summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        if (_audio is null) { StatusText = "宿主文件服务不可用"; return; }
        if (string.IsNullOrWhiteSpace(FolderPath)) { StatusText = "请填写目标文件夹路径"; return; }
        if (!ExportLyrics && !ExportCover) { StatusText = "请至少选择导出歌词或封面"; return; }
        // 桌面端路径为真实目录；Android SAF 返回 content:// URI 无法用 File I/O
        if (!Directory.Exists(FolderPath)) { StatusText = "目标文件夹不存在或不可写（Android SAF 路径需桌面端）"; return; }

        IsBusy = true;
        Results.Clear();
        DoneCount = 0;
        int lyricsOk = 0, coverOk = 0, skip = 0;
        try
        {
            foreach (var song in _songs)
            {
                var item = new ExportResultItem { Title = song.Title, Artist = song.Artist };
                try
                {
                    var tags = await _audio.ReadTagsAsync(song.FilePath);
                    if (tags == null) { item.Result = "读取标签失败"; skip++; Results.Add(item); DoneCount++; continue; }

                    var baseName = SanitizeFileName(string.IsNullOrWhiteSpace(tags.Title)
                        ? song.Title : tags.Title);
                    var artist = SanitizeFileName(string.IsNullOrWhiteSpace(tags.Artist)
                        ? song.Artist : tags.Artist);
                    var name = string.IsNullOrWhiteSpace(artist) ? baseName : $"{baseName} - {artist}";

                    var parts = new List<string>();
                    if (ExportLyrics && !string.IsNullOrWhiteSpace(tags.Lyrics))
                    {
                        var path = ResolveUnique(FolderPath, name + ".lrc");
                        await File.WriteAllTextAsync(path, tags.Lyrics!);
                        lyricsOk++;
                        parts.Add("歌词✓");
                    }
                    if (ExportCover && tags.Cover is { Length: > 0 })
                    {
                        var ext = tags.Cover![0] == 0x89 ? ".png" : ".jpg";
                        var path = ResolveUnique(FolderPath, name + ext);
                        await File.WriteAllBytesAsync(path, tags.Cover!);
                        coverOk++;
                        parts.Add("封面✓");
                    }
                    item.Result = parts.Count > 0 ? string.Join(" ", parts) : "无歌词/封面";
                    if (parts.Count == 0) skip++;
                }
                catch (Exception ex)
                {
                    item.Result = "失败：" + ex.Message;
                    skip++;
                }
                Results.Add(item);
                DoneCount++;
                StatusText = $"导出中… {DoneCount}/{TotalSongs}";
            }
            StatusText = $"完成：歌词 {lyricsOk} · 封面 {coverOk} · 跳过 {skip} / 共 {TotalSongs}";
        }
        finally { IsBusy = false; }
    }

    /// <summary>文件名安全化：替换非法字符、去首尾点。</summary>
    private static string SanitizeFileName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "未知";
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Trim().Trim('.');
    }

    /// <summary>若目标文件已存在，追加 (2)/(3) 防覆盖。</summary>
    private static string ResolveUnique(string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path)) return path;
        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (int i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}

/// <summary>导出结果项（歌曲 + 结果文本）。</summary>
public class ExportResultItem
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Result { get; set; } = "";
    public string Display => $"{Title} - {Artist}";
}
