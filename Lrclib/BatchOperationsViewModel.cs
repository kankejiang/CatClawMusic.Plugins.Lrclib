using CatClawMusic.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>批量操作模式</summary>
public enum BatchOperationMode
{
    /// <summary>批量匹配歌词（逐首自动搜索 LRCLIB 并写入最佳匹配）</summary>
    MatchLyrics,

    /// <summary>批量编辑标签（统一字段应用到所有选中歌曲）</summary>
    EditTags,

    /// <summary>批量重命名文件（按格式占位符改名，保留扩展名，不影响标签）</summary>
    RenameFiles,

    /// <summary>批量删除文件（永久删除，不可恢复）</summary>
    DeleteFiles,
}

/// <summary>
/// 批量操作 ViewModel（Lyrico SongSelection 复刻）：
/// 接收多选歌曲列表，支持 批量匹配歌词、批量编辑标签、批量重命名文件、批量删除。
/// 进度逐首显示，失败/无匹配的歌曲记录在结果列表，不影响其余继续。
/// </summary>
public partial class BatchOperationsViewModel : ObservableObject
{
    private readonly IAudioFileService? _audio;
    private readonly LrclibApiClient _client;

    public BatchOperationMode Mode { get; }
    public IReadOnlyList<SongItem> Songs { get; }

    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private int doneCount;
    [ObservableProperty] private string progressText = "";
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private bool hasResults;
    [ObservableProperty] private bool canRun = true;

    /// <summary>逐首处理结果（歌曲 + 状态）</summary>
    public ObservableCollection<BatchResultItem> Results { get; } = new();

    // 批量编辑字段（与 EditMetadataViewModel 一致）
    [ObservableProperty] private string editTitle = "";
    [ObservableProperty] private string editArtist = "";
    [ObservableProperty] private string editAlbum = "";
    [ObservableProperty] private string editYear = "";
    [ObservableProperty] private string editGenre = "";
    [ObservableProperty] private bool editOnlyFilled = true;

    // 批量重命名字段：@1标题 @2艺人 @3专辑艺人 @4专辑 @5音轨号 @6碟号 @7年份 @8流派
    [ObservableProperty] private string renameFormat = "@1 - @2";
    [ObservableProperty] private string renamePreviewText = "";

    /// <summary>重命名占位符说明（供 UI 辅助提示）</summary>
    public static string[] PlaceholderHints => new[]
    {
        "@1 标题", "@2 艺人", "@3 专辑艺人", "@4 专辑",
        "@5 音轨号", "@6 碟号", "@7 年份", "@8 流派",
    };

    public BatchOperationsViewModel(IReadOnlyList<SongItem> songs, BatchOperationMode mode,
        IAudioFileService? audio, LrclibApiClient client)
    {
        Songs = songs;
        Mode = mode;
        _audio = audio;
        _client = client;

        StatusText = mode switch
        {
            BatchOperationMode.MatchLyrics => $"共 {songs.Count} 首待匹配，点「开始匹配」逐首搜索 LRCLIB 并写入",
            BatchOperationMode.EditTags => $"共 {songs.Count} 首待编辑，填写要统一的字段后点「应用」",
            BatchOperationMode.RenameFiles => $"共 {songs.Count} 首待重命名，设置格式后点「重命名」",
            BatchOperationMode.DeleteFiles => $"共 {songs.Count} 首待删除，此操作不可恢复",
            _ => "",
        };
    }

    partial void OnRenameFormatChanged(string value) => RefreshRenamePreview();

    /// <summary>预览前几首新文件名</summary>
    public void RefreshRenamePreview()
    {
        if (Mode != BatchOperationMode.RenameFiles) return;
        var lines = Songs.Take(5)
            .Select(s => $"{Path.GetFileName(s.Song.FilePath)}  →  {BuildFileName(s)}")
            .ToList();
        RenamePreviewText = lines.Count == 0 ? "无歌曲" : string.Join("\n", lines);
    }

    public bool HasAudio => _audio is not null;

    [RelayCommand]
    private async Task RunAsync()
    {
        if (IsRunning || Songs.Count == 0) return;
        IsRunning = true;
        CanRun = false;
        Results.Clear();
        HasResults = false;
        DoneCount = 0;

        try
        {
            switch (Mode)
            {
                case BatchOperationMode.MatchLyrics: await RunMatchLyricsAsync(); break;
                case BatchOperationMode.EditTags: await RunEditTagsAsync(); break;
                case BatchOperationMode.RenameFiles: await RunRenameAsync(); break;
                case BatchOperationMode.DeleteFiles: await RunDeleteAsync(); break;
            }
        }
        finally
        {
            IsRunning = false;
            CanRun = true;   // 恢复执行按钮，否则一次执行后永久禁用
        }

        HasResults = true;
        ProgressText = $"完成 {DoneCount}/{Songs.Count}";

        // 把本次批量操作记录为一条持久化历史任务（任务中心可查看明细）。
        if (Mode != BatchOperationMode.DeleteFiles)
        {
            BatchTaskStore.Add(new BatchTaskRecord
            {
                Mode = ModeDisplayName(),
                Total = Songs.Count,
                SuccessCount = Results.Count(r => r.Success),
                Items = Results.Select(r => new BatchTaskItemRecord
                {
                    Title = r.Title,
                    Status = r.Status,
                    Success = r.Success,
                }).ToList(),
            });
        }
    }

    private string ModeDisplayName() => Mode switch
    {
        BatchOperationMode.MatchLyrics => "批量匹配歌词",
        BatchOperationMode.EditTags => "批量编辑标签",
        BatchOperationMode.RenameFiles => "批量重命名文件",
        BatchOperationMode.DeleteFiles => "批量删除文件",
        _ => "批量操作",
    };

    // ───────────────── 批量重命名 ─────────────────

    /// <summary>按格式占位符生成新文件名（含扩展名），未匹配占位符部分清空</summary>
    private string BuildFileName(SongItem song)
    {
        var s = song.Song;
        var displayName = Path.GetFileName(s.FilePath);
        if (string.IsNullOrEmpty(displayName)) displayName = s.Title;
        var format = string.IsNullOrWhiteSpace(RenameFormat) ? "@1 - @2" : RenameFormat;
        var name = format
            .Replace("@1", s.Title)
            .Replace("@2", s.Artist)
            .Replace("@3", s.Artist)        // 专辑艺人：未单独存储，退化为艺人
            .Replace("@4", s.Album ?? "")
            .Replace("@5", s.TrackNumber > 0 ? s.TrackNumber.ToString("D2") : "")
            .Replace("@6", "")
            .Replace("@7", s.Year > 0 ? s.Year.ToString() : "")
            .Replace("@8", s.Genre ?? "");

        name = SanitizeFileName(name);
        var ext = GetExtension(displayName);
        return string.IsNullOrWhiteSpace(name) ? displayName : $"{name}{ext}";
    }

    private async Task RunRenameAsync()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var song in Songs)
        {
            if (string.IsNullOrWhiteSpace(song.FilePath))
            {
                Results.Add(new BatchResultItem(song, false, "无本地路径"));
                DoneCount++;
                continue;
            }

            try
            {
                var displayName = Path.GetFileName(song.FilePath) ?? song.Title;
                var newName = BuildFileName(song);
                if (string.Equals(newName, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    Results.Add(new BatchResultItem(song, true, "文件名未变"));
                    DoneCount++;
                    continue;
                }

                // 冲突自动加序号后缀 (1)(2)…
                var unique = newName;
                var cnt = 1;
                while (!used.Add(unique))
                {
                    var baseName = Path.GetFileNameWithoutExtension(newName);
                    var ext = Path.GetExtension(newName);
                    unique = $"{baseName} ({cnt}){ext}";
                    cnt++;
                }

                var newUri = await _audio!.RenameFileAsync(song.FilePath, unique);
                Results.Add(new BatchResultItem(song, newUri != null, newUri != null ? $"已改为 {unique}" : "重命名失败"));
            }
            catch (Exception ex)
            {
                Results.Add(new BatchResultItem(song, false, $"异常：{ex.Message}"));
            }
            DoneCount++;
            ProgressText = $"{DoneCount}/{Songs.Count} · {song.Title}";
        }
    }

    // ───────────────── 批量删除 ─────────────────

    private async Task RunDeleteAsync()
    {
        foreach (var song in Songs)
        {
            if (string.IsNullOrWhiteSpace(song.FilePath))
            {
                Results.Add(new BatchResultItem(song, false, "无本地路径"));
                DoneCount++;
                continue;
            }

            try
            {
                var ok = await _audio!.DeleteFileAsync(song.FilePath);
                Results.Add(new BatchResultItem(song, ok, ok ? "已删除" : "删除失败"));
            }
            catch (Exception ex)
            {
                Results.Add(new BatchResultItem(song, false, $"异常：{ex.Message}"));
            }
            DoneCount++;
            ProgressText = $"{DoneCount}/{Songs.Count} · {song.Title}";
        }
    }

    // ─── 文件名工具 ───

    private static string GetExtension(string displayName)
    {
        var ext = Path.GetExtension(displayName);
        return string.IsNullOrEmpty(ext) ? "" : ext;
    }

    /// <summary>按字符映射规则替换文件名非法字符（默认全角等价符，可在字符映射页自定义）+ 清理首尾空白</summary>
    internal static string SanitizeFileName(string name)
    {
        var map = new CharacterMappingStore().GetMapping();
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(map.TryGetValue(ch, out var to) ? to : ch);
        return sb.ToString().Trim();
    }

    private async Task RunMatchLyricsAsync()
    {
        foreach (var song in Songs)
        {
            var result = await TryMatchOneAsync(song);
            Results.Add(result);
            DoneCount++;
            ProgressText = $"{DoneCount}/{Songs.Count} · {song.Title}";
        }
    }

    private async Task<BatchResultItem> TryMatchOneAsync(SongItem song)
    {
        if (_audio is null || string.IsNullOrWhiteSpace(song.FilePath))
            return new BatchResultItem(song, false, "无写文件服务或路径");

        try
        {
            // 优先精确匹配（带时长），失败再搜索取最佳候选
            var durationSeconds = NormalizeDuration(song.Song.Duration);
            var match = await _client.GetAsync(song.Title, song.Artist, song.Song.Album, durationSeconds)
                        ?? await PickBestAsync(song.Title, song.Artist, durationSeconds);

            if (match == null)
                return new BatchResultItem(song, false, "LRCLIB 未收录");

            var lyrics = !string.IsNullOrWhiteSpace(match.SyncedLyrics)
                ? match.SyncedLyrics
                : match.PlainLyrics;
            if (string.IsNullOrWhiteSpace(lyrics))
                return new BatchResultItem(song, false, "候选无歌词");

            var ok = await _audio.WriteTagsAsync(song.FilePath, new CatClawMusic.Core.Models.AudioTagEdit
            {
                Lyrics = lyrics,
            });
            return new BatchResultItem(song, ok, ok ? "已写入" : "写入失败");
        }
        catch (Exception ex)
        {
            return new BatchResultItem(song, false, $"异常：{ex.Message}");
        }
    }

    /// <summary>搜索并挑最佳候选（有歌词优先，时长相近优先）</summary>
    private async Task<LrclibTrack?> PickBestAsync(string title, string artist, double durationSeconds)
    {
        var candidates = await _client.SearchAsync(title, artist, null, durationSeconds);
        if (candidates == null || candidates.Count == 0) return null;

        LrclibTrack? best = null;
        double bestScore = -1;
        foreach (var c in candidates)
        {
            if (c.Instrumental || !c.HasLyricsText()) continue;

            double score = 0;
            if (string.Equals(c.TrackName?.Trim(), title, StringComparison.OrdinalIgnoreCase)) score += 2;
            if (durationSeconds > 0) score += Math.Max(0, 10 - Math.Abs(c.Duration - durationSeconds));
            if (!string.IsNullOrWhiteSpace(c.SyncedLyrics)) score += 1;

            if (score > bestScore + 0.001)
            {
                bestScore = score;
                best = c;
            }
        }
        return best;
    }

    /// <summary>批量编辑：把填写的字段统一应用到所有歌曲（仅应用非空字段，除非关掉「仅填写项」）</summary>
    private async Task RunEditTagsAsync()
    {
        foreach (var song in Songs)
        {
            if (string.IsNullOrWhiteSpace(song.FilePath))
            {
                Results.Add(new BatchResultItem(song, false, "无本地路径"));
                DoneCount++;
                continue;
            }

            try
            {
                var edit = new CatClawMusic.Core.Models.AudioTagEdit();
                bool any = false;

                if (TryField(EditTitle, out var t)) { edit.Title = t; any = true; }
                if (TryField(EditArtist, out var a)) { edit.Artist = a; any = true; }
                if (TryField(EditAlbum, out var al)) { edit.Album = al; any = true; }
                if (TryField(EditYear, out var y)) { edit.Year = y; any = true; }
                if (TryField(EditGenre, out var g)) { edit.Genre = g; any = true; }

                if (!any)
                {
                    Results.Add(new BatchResultItem(song, false, "未填写字段"));
                    DoneCount++;
                    continue;
                }

                var ok = await _audio!.WriteTagsAsync(song.FilePath, edit);
                Results.Add(new BatchResultItem(song, ok, ok ? "已更新" : "写入失败"));
            }
            catch (Exception ex)
            {
                Results.Add(new BatchResultItem(song, false, $"异常：{ex.Message}"));
            }
            DoneCount++;
            ProgressText = $"{DoneCount}/{Songs.Count} · {song.Title}";
        }
    }

    /// <summary>按「仅填写项」策略判断字段是否生效</summary>
    private bool TryField(string value, out string? result)
    {
        result = null;
        if (EditOnlyFilled)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            result = value.Trim();
            return true;
        }
        result = value; // 允许清空
        return true;
    }

    /// <summary>时长归一化为秒（宿主 Song.Duration 可能是毫秒）</summary>
    private static double NormalizeDuration(int duration)
    {
        if (duration <= 0) return 0;
        return duration > 1000 ? duration / 1000.0 : duration;
    }
}

/// <summary>批量处理单首歌曲的结果</summary>
public class BatchResultItem
{
    public string Title { get; }
    public string Subtitle { get; }
    public bool Success { get; }
    public string Status { get; }

    public BatchResultItem(SongItem song, bool success, string status)
    {
        Title = song.Title;
        Subtitle = song.Artist;
        Success = success;
        Status = status;
    }

    public string StatusColor => Success ? "#4ADE80" : "#F87171";
}

/// <summary>LrclibTrack 歌词存在性扩展</summary>
internal static class LrclibTrackExt
{
    public static bool HasLyricsText(this LrclibTrack t)
        => !string.IsNullOrWhiteSpace(t.SyncedLyrics) || !string.IsNullOrWhiteSpace(t.PlainLyrics);
}
