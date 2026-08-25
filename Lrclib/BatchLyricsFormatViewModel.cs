using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 批量歌词格式 ViewModel（复刻 Lyrico <c>BatchLyricsFormatViewModel</c>）：
/// 逐首读取内嵌歌词 → 检测格式 → 转换为目标格式（LRC / Enhanced LRC / TTML）→ 写回。
/// 支持去空行、过滤标签行；无内嵌歌词或已为目标格式的歌曲记录在结果中，不影响其余继续。
/// </summary>
public partial class BatchLyricsFormatViewModel : ObservableObject
{
    private readonly IAudioFileService? _audio;

    public IReadOnlyList<SongItem> Songs { get; }

    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private int doneCount;
    [ObservableProperty] private string progressText = "";
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private bool hasResults;
    [ObservableProperty] private bool canRun = true;

    [ObservableProperty] private int targetFormatIndex;
    [ObservableProperty] private bool removeEmptyLines;
    [ObservableProperty] private bool removeTagLines;

    /// <summary>目标格式可选项（下标与 <see cref="LyricFormat"/> 一致）</summary>
    public string[] TargetFormatOptions { get; } = { "普通 LRC", "增强 LRC（词级）", "TTML" };

    public ObservableCollection<BatchResultItem> Results { get; } = new();

    /// <summary>标签行关键词，可按逗号/顿号分隔</summary>
    [ObservableProperty] private string tagKeywords = "[ar: [al: [offset: [by: [re: [ve:";

    public BatchLyricsFormatViewModel(IReadOnlyList<SongItem> songs, IAudioFileService? audio)
    {
        Songs = songs;
        _audio = audio;
        StatusText = $"共 {songs.Count} 首待处理，选择目标格式后点「开始转换」";
    }

    public bool HasAudio => _audio is not null;

    public LyricFormat TargetFormat => TargetFormatIndex switch
    {
        1 => LyricFormat.EnhancedLrc,
        2 => LyricFormat.Ttml,
        _ => LyricFormat.PlainLrc,
    };

    [RelayCommand]
    private async Task RunAsync()
    {
        if (IsRunning || Songs.Count == 0) return;
        IsRunning = true;
        CanRun = false;
        Results.Clear();
        HasResults = false;
        DoneCount = 0;

        var target = TargetFormat;
        var tagKws = RemoveTagLines
            ? TagKeywords.Split(new[] { ',', '，', '、', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim()).Where(k => k.Length > 0).ToList()
            : null;

        foreach (var song in Songs)
        {
            Results.Add(await ConvertOneAsync(song, target, RemoveEmptyLines, tagKws));
            DoneCount++;
            ProgressText = $"{DoneCount}/{Songs.Count} · {song.Title}";
        }

        IsRunning = false;
        HasResults = true;
        ProgressText = $"完成 {DoneCount}/{Songs.Count}";
    }

    private async Task<BatchResultItem> ConvertOneAsync(SongItem song, LyricFormat target,
        bool removeEmpty, List<string>? tagKws)
    {
        if (_audio is null || string.IsNullOrWhiteSpace(song.FilePath))
            return new BatchResultItem(song, false, "无写文件服务或路径");

        try
        {
            var info = await _audio.ReadTagsAsync(song.FilePath);
            var lyrics = info?.Lyrics;
            if (string.IsNullOrWhiteSpace(lyrics))
                return new BatchResultItem(song, false, "无内嵌歌词");

            var current = LyricFormatConverter.DetectFormat(lyrics);
            var converted = LyricFormatConverter.Convert(lyrics, target, removeEmpty, tagKws);

            // 无实际变化（已为目标格式且无文字清洗）→ 跳过
            if (current == target &&
                !removeEmpty && (tagKws == null || tagKws.Count == 0) &&
                string.Equals(converted, lyrics, StringComparison.Ordinal))
                return new BatchResultItem(song, true, "已是目标格式，未修改");

            var ok = await _audio.WriteTagsAsync(song.FilePath, new AudioTagEdit { Lyrics = converted });
            return new BatchResultItem(song, ok, ok ? $"已转为 {TargetFormatLabel(target)}" : "写入失败");
        }
        catch (Exception ex)
        {
            return new BatchResultItem(song, false, $"异常：{ex.Message}");
        }
    }

    private static string TargetFormatLabel(LyricFormat f) => f switch
    {
        LyricFormat.EnhancedLrc => "增强 LRC",
        LyricFormat.Ttml => "TTML",
        _ => "普通 LRC",
    };
}