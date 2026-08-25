using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 批量标签导出/导入（复刻 Lyrico BatchExport 的场景）：
/// 把多首歌曲的标准标签/歌词导出为 JSON 或 CSV 到应用数据目录（卸载后仍可访问），
/// 或从之前导出的文件导入，按 文件路径/标题 匹配写回标签。
/// 纯插件实现，复用宿主 <see cref="IAudioFileService"/> 读/写。
/// </summary>
public partial class BatchTagTransferViewModel : ObservableObject
{
    private readonly IAudioFileService? _audio;

    public IReadOnlyList<SongItem> Songs { get; }

    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private int doneCount;
    [ObservableProperty] private string progressText = "";
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private bool canRun = true;

    [ObservableProperty] private int modeIndex;
    [ObservableProperty] private string? selectedExportFile;

    public ObservableCollection<BatchResultItem> Results { get; } = new();

    public string[] ModeOptions { get; } = { "导出标签", "导入标签" };

    /// <summary>导出文件存放目录（应用数据，卸载应用备份仍保留）</summary>
    private static readonly string ExportDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CatClawMusic.Maui", "tag_exports");

    public BatchTagTransferViewModel(IReadOnlyList<SongItem> songs, IAudioFileService? audio)
    {
        Songs = songs;
        _audio = audio;
        StatusText = $"共 {songs.Count} 首待处理";
        InitLists();
    }

    private void InitLists() => SelectedExportFile = GetExportFiles().FirstOrDefault();

    /// <summary>已导出的标签文件列表</summary>
    public List<string> GetExportFiles()
    {
        try
        {
            if (!Directory.Exists(ExportDir)) return new List<string>();
            return Directory.GetFiles(ExportDir, "*.*")
                .Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                            || f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .ToList();
        }
        catch { return new List<string>(); }
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (IsRunning || Songs.Count == 0) return;
        IsRunning = true;
        CanRun = false;
        Results.Clear();
        DoneCount = 0;

        try
        {
            if (ModeIndex == 0 && _audio != null) await RunExportAsync();
            else if (ModeIndex == 1 && _audio != null) await RunImportAsync();
            else StatusText = "宿主未提供写文件服务";
        }
        finally
        {
            IsRunning = false;
            CanRun = true;
            ProgressText = $"完成 {DoneCount}/{Songs.Count}";
        }
    }

    // ═══════════════ 导出 ═══════════════

    private async Task RunExportAsync()
    {
        var list = new List<object>();
        foreach (var song in Songs)
        {
            var info = await SafeReadAsync(song);
            if (info == null)
            {
                Results.Add(new BatchResultItem(song, false, "读取失败"));
            }
            else
            {
                list.Add(ExportRow(info));
                Results.Add(new BatchResultItem(song, true, "已读取"));
            }
            DoneCount++;
            ProgressText = $"{DoneCount}/{Songs.Count} · {song.Title}";
        }

        var ok = WriteExportFile(list);
        if (ok)
            StatusText = $"已导出 {list.Count} 首 → {SelectedExportFile}";
        else
            StatusText = "导出失败（无法写入导出目录）";
    }

    private static Dictionary<string, object?> ExportRow(AudioTagInfo t) => new()
    {
        ["path"] = t.FilePath,
        ["title"] = t.Title,
        ["artist"] = t.Artist,
        ["album"] = t.Album,
        ["albumArtist"] = t.AlbumArtist,
        ["year"] = t.Year,
        ["genre"] = t.Genre,
        ["track"] = t.TrackNumber,
        ["disc"] = t.DiscNumber,
        ["composer"] = t.Composer,
        ["lyricist"] = t.Lyricist,
        ["comment"] = t.Comment,
        ["copyright"] = t.Copyright,
        ["customTags"] = t.CustomTags,
        ["lyrics"] = t.Lyrics,
    };

    private bool WriteExportFile(List<object> rows)
    {
        try
        {
            Directory.CreateDirectory(ExportDir);
            SelectedExportFile = Path.Combine(ExportDir, $"tag_export_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            var json = JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SelectedExportFile, json, new UTF8Encoding(false));
            return true;
        }
        catch { return false; }
    }

    // ═══════════════ 导入 ═══════════════

    private async Task RunImportAsync()
    {
        if (string.IsNullOrEmpty(SelectedExportFile))
        {
            StatusText = "没有可导入的导出文件";
            return;
        }

        var rows = LoadRows(SelectedExportFile!);
        if (rows == null)
        {
            StatusText = "导入文件解析失败";
            return;
        }

        var targets = rows
            .Select(r => new
            {
                Path = GetStr(r, "path"),
                Title = GetStr(r, "title"),
                Data = r,
            })
            .ToList();

        foreach (var song in Songs)
        {
            var match = targets.FirstOrDefault(t =>
                !string.IsNullOrEmpty(t.Path) &&
                string.Equals(t.Path, song.FilePath, StringComparison.OrdinalIgnoreCase))
                ?? targets.FirstOrDefault(t =>
                    !string.IsNullOrEmpty(t.Title) &&
                    string.Equals(t.Title, song.Title, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                Results.Add(new BatchResultItem(song, false, "未匹配到导出记录"));
            }
            else
            {
                var edit = RowToEdit(match.Data);
                var ok = await SafeWriteAsync(song, edit);
                Results.Add(new BatchResultItem(song, ok, ok ? "已导入" : "写入失败"));
            }
            DoneCount++;
            ProgressText = $"{DoneCount}/{Songs.Count} · {song.Title}";
        }
    }

    private static List<Dictionary<string, JsonElement>>? LoadRows(string file)
    {
        try
        {
            var text = File.ReadAllText(file, Encoding.UTF8);
            var doc = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(text);
            return doc;
        }
        catch { return null; }
    }

    private static string GetStr(Dictionary<string, JsonElement> row, string key)
    {
        if (!row.TryGetValue(key, out var el)) return string.Empty;
        if (el.ValueKind == JsonValueKind.String) return el.GetString() ?? string.Empty;
        return string.Empty;
    }

    private static AudioTagEdit RowToEdit(Dictionary<string, JsonElement> row)
    {
        var edit = new AudioTagEdit
        {
            Title = GetStr(row, "title"),
            Artist = GetStr(row, "artist"),
            Album = GetStr(row, "album"),
            AlbumArtist = GetStr(row, "albumArtist"),
            Year = GetStr(row, "year"),
            Genre = GetStr(row, "genre"),
            TrackNumber = GetStr(row, "track"),
            DiscNumber = GetStr(row, "disc"),
            Composer = GetStr(row, "composer"),
            Lyricist = GetStr(row, "lyricist"),
            Comment = GetStr(row, "comment"),
            Copyright = GetStr(row, "copyright"),
            Lyrics = GetStr(row, "lyrics"),
        };

        if (row.TryGetValue("customTags", out var el) && el.ValueKind == JsonValueKind.Object)
        {
            var custom = new Dictionary<string, string>();
            foreach (var prop in el.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                    custom[prop.Name] = prop.Value.GetString() ?? "";
            }
            if (custom.Count > 0) edit.CustomTags = custom;
        }

        return edit;
    }

    private async Task<AudioTagInfo?> SafeReadAsync(SongItem song)
    {
        try
        {
            if (_audio is null || string.IsNullOrWhiteSpace(song.FilePath)) return null;
            return await _audio.ReadTagsAsync(song.FilePath);
        }
        catch { return null; }
    }

    private async Task<bool> SafeWriteAsync(SongItem song, AudioTagEdit edit)
    {
        try
        {
            if (_audio is null || string.IsNullOrWhiteSpace(song.FilePath)) return false;
            return await _audio.WriteTagsAsync(song.FilePath, edit);
        }
        catch { return false; }
    }
}