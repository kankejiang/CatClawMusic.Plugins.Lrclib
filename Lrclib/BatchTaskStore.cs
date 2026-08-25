using System.Text.Json;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 批量任务历史持久化存储：把每次执行的批量操作（兼歌词/编辑/重命名/删除等）记录为一条
/// 历史任务（含逐首明细），以 JSON 落盘。对应 Lyrico 的 BatchTask 持久化（Room）在插件
/// 单 DLL 环境下的轻量近似。
/// </summary>
public static class BatchTaskStore
{
    private static readonly object _lock = new();
    private static List<BatchTaskRecord>? _cache;

    private static string FilePath
    {
        get
        {
            try { return Path.Combine(FileSystem.AppDataDirectory, "Plugin", "batch_tasks.json"); }
            catch { return Path.Combine(AppContext.BaseDirectory, "batch_tasks.json"); }
        }
    }

    /// <summary>读取全部历史任务（新→旧）。</summary>
    public static List<BatchTaskRecord> GetAll()
    {
        lock (_lock) return Load().ToList();
    }

    /// <summary>追加一条历史任务并落盘。</summary>
    public static void Add(BatchTaskRecord record)
    {
        lock (_lock)
        {
            var list = Load();
            list.Insert(0, record);
            if (list.Count > 100) list.RemoveRange(100, list.Count - 100);  // 上限 100 条
            Save(list);
            _cache = list;
        }
    }

    /// <summary>清空全部历史任务。</summary>
    public static void Clear()
    {
        lock (_lock)
        {
            Save(new List<BatchTaskRecord>());
            _cache = null;
        }
    }

    private static List<BatchTaskRecord> Load()
    {
        if (_cache != null) return _cache;
        try
        {
            var path = FilePath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var list = JsonSerializer.Deserialize<List<BatchTaskRecord>>(json, JsonOpts);
                if (list != null) return _cache = list;
            }
        }
        catch { }
        return _cache = new List<BatchTaskRecord>();
    }

    private static void Save(List<BatchTaskRecord> list)
    {
        try
        {
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(list, JsonOpts));
        }
        catch { }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };
}

/// <summary>批量任务历史记录（一条 = 一次批量操作）。</summary>
public class BatchTaskRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Mode { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int Total { get; set; }
    public int SuccessCount { get; set; }
    public int FailCount => Total - SuccessCount;
    public bool AllSuccess => Total > 0 && SuccessCount == Total;
    public List<BatchTaskItemRecord> Items { get; set; } = new();

    public string TimeText => CreatedAt.ToString("MM-dd HH:mm");
    public string Summary => $"{Mode} · {SuccessCount}/{Total} 成功";
    public string Color => SuccessCount == Total ? "#4ADE80" : "#F87171";
}

/// <summary>批量任务中单首歌曲的明细。</summary>
public class BatchTaskItemRecord
{
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public bool Success { get; set; }
    public string StatusColor => Success ? "#4ADE80" : "#F87171";
}