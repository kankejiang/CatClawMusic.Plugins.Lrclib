using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 批量响度 ReplayGain（复刻 Lyrico 批量响度扫描场景）：
/// 用宿主 <see cref="ILoudnessAnalyzer"/>（FFmpeg ebur128）逐首分析 EBU R128 响度，
/// 换算为 ReplayGain 增益并把 REPLAYGAIN_TRACK_GAIN / REPLAYGAIN_TRACK_PEAK 写回标签。
/// 宿主未提供分析服务时给出降级提示。
/// </summary>
public partial class BatchReplayGainViewModel : ObservableObject
{
    private readonly IAudioFileService? _audio;
    private readonly ILoudnessAnalyzer? _analyzer;

    public IReadOnlyList<SongItem> Songs { get; }

    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private int doneCount;
    [ObservableProperty] private string progressText = "";
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private bool canRun = true;

    public ObservableCollection<BatchResultItem> Results { get; } = new();

    public BatchReplayGainViewModel(IReadOnlyList<SongItem> songs, IAudioFileService? audio, ILoudnessAnalyzer? analyzer)
    {
        Songs = songs;
        _audio = audio;
        _analyzer = analyzer;
        StatusText = _analyzer == null
            ? "宿主未提供 FFmpeg 响度分析服务，本功能不可用"
            : $"共 {songs.Count} 首待分析";
        CanRun = _analyzer != null;
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (IsRunning || _analyzer == null || Songs.Count == 0) return;
        IsRunning = true;
        CanRun = false;
        Results.Clear();
        DoneCount = 0;

        try
        {
            foreach (var song in Songs)
            {
                var (ok, status) = await ProcessOneAsync(song);
                Results.Add(new BatchResultItem(song, ok, status));
                DoneCount++;
                ProgressText = $"{DoneCount}/{Songs.Count} · {song.Title}";
            }
        }
        finally
        {
            IsRunning = false;
            CanRun = true;
            StatusText = _analyzer != null ? $"完成 {DoneCount}/{Songs.Count}" : StatusText;
            ProgressText = $"完成 {DoneCount}/{Songs.Count}";
        }
    }

    private async Task<(bool ok, string status)> ProcessOneAsync(SongItem song)
    {
        if (string.IsNullOrWhiteSpace(song.FilePath)) return (false, "无文件路径");
        try
        {
            var result = await _analyzer!.AnalyzeAsync(song.FilePath);
            if (result == null) return (false, "分析失败（FFmpeg 不可用或格式不支持）");

            if (_audio == null) return (false, "宿主未提供写文件服务");
            var edit = new AudioTagEdit
            {
                CustomTags = new Dictionary<string, string>
                {
                    ["REPLAYGAIN_TRACK_GAIN"] = result.TrackGainTag,
                    ["REPLAYGAIN_TRACK_PEAK"] = result.TrackPeakTag,
                },
            };
            var ok = await _audio.WriteTagsAsync(song.FilePath, edit);
            return ok
                ? (true, $"增益 {result.TrackGainDb:+#.##;-#.##;0.00} dB · 峰值 {result.TrackPeak:0.###}")
                : (false, "标签写入失败");
        }
        catch
        {
            return (false, "处理异常");
        }
    }
}