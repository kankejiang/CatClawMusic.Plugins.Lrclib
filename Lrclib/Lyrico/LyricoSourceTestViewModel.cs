using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Plugins.Lrclib.Lyrico;

/// <summary>
/// Lyrico 源测试 ViewModel：输入歌名/艺人/专辑/时长 → 调用指定源的 getLyrics →
/// 显示命中歌词行数 + 前若干行预览，验证导入+配置后该源取词是否正常。
/// </summary>
public partial class LyricoSourceTestViewModel : ObservableObject
{
    private readonly LyricoLyricsHub _hub;
    private readonly string _pluginDir;

    [ObservableProperty] private string sourceName = "";
    [ObservableProperty] private string testTitle = "";
    [ObservableProperty] private string testArtist = "";
    [ObservableProperty] private string testAlbum = "";
    [ObservableProperty] private string testDuration = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private string previewText = "";
    [ObservableProperty] private bool hasResult;

    public LyricoSourceTestViewModel(LyricoLyricsHub hub, string pluginDir)
    {
        _hub = hub;
        _pluginDir = pluginDir;
        var manifest = hub.GetManifest(pluginDir);
        SourceName = manifest?.Name ?? pluginDir;
    }

    /// <summary>执行测试取词。</summary>
    [RelayCommand]
    private async Task RunTestAsync()
    {
        if (string.IsNullOrWhiteSpace(TestTitle))
        {
            StatusText = "请输入歌名";
            return;
        }
        IsBusy = true;
        StatusText = "取词中...";
        PreviewText = "";
        HasResult = false;
        try
        {
            _ = double.TryParse(TestDuration, out var dur);
            var lyrics = await _hub.TestSourceAsync(_pluginDir, TestTitle.Trim(),
                TestArtist.Trim(), TestAlbum.Trim(), dur);
            if (lyrics == null || lyrics.Lines.Count == 0)
            {
                // 展示真实失败原因（引擎/脚本装载错误），而不是笼统提示
                var loadError = _hub.GetSourceLoadError(_pluginDir);
                StatusText = loadError != null
                    ? $"未取到歌词（源执行异常：{loadError}）"
                    : "未取到歌词（源无结果 / 配置缺失 / 网络问题）";
                return;
            }
            HasResult = true;
            StatusText = $"命中 {lyrics.Lines.Count} 行歌词" +
                (lyrics.Metadata != null && !string.IsNullOrEmpty(lyrics.Metadata.Title)
                    ? $"（{lyrics.Metadata.Title}）" : "");
            // 预览前 20 行（带时间戳）
            var sb = new System.Text.StringBuilder();
            foreach (var line in lyrics.Lines.Take(20))
            {
                var t = line.Timestamp;
                sb.Append($"[{(int)t.TotalMinutes:D2}:{t.Seconds:D2}] {line.Text}\n");
            }
            if (lyrics.Lines.Count > 20) sb.Append($"...（共 {lyrics.Lines.Count} 行）");
            PreviewText = sb.ToString();
        }
        catch (Exception ex)
        {
            StatusText = "测试失败：" + ex.Message;
        }
        finally { IsBusy = false; }
    }
}
