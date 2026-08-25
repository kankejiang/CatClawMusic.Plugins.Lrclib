using System.Text.Json;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Plugins.Lrclib.Lyrico;

/// <summary>
/// Lyrico 多源编排：依次尝试内嵌的 5 个 JS 源（netease/qq/kugou/soda/apple），
/// 对每个源取回歌词候选并按 歌名一致性+时长相近 择优，返回首个命中的源结果。
/// 仅作为 LRCLIB 兜底链的一部分被调用。
/// </summary>
public sealed class LyricoLyricsHub : IDisposable
{
    /// <summary>源尝试顺序：国内源优先，apple 依赖第三方/开发者令牌常为 no-op，放最后。</summary>
    private static readonly string[] PriorityOrder = { "netease", "qq", "kugou", "soda", "apple" };

    private LyricoSourceCatalog _catalog;
    private readonly Dictionary<string, LyricoScriptHost> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _initLock = new();
    private bool _initialized;
    private string[] _order;
    /// <summary>用户禁用的源目录名集合（持久化，GetAsync 跳过）。</summary>
    private HashSet<string> _disabled = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>各源是否已加载（调试/诊断用）。</summary>
    public IReadOnlyDictionary<string, string> SourceStatuses { get; private set; } =
        new Dictionary<string, string>();

    public LyricoLyricsHub()
    {
        _catalog = new LyricoSourceCatalog();
        _order = ResolveOrder(_catalog);
        _disabled = LoadDisabled();
    }

    /// <summary>解析尝试顺序：官方源在前（保持兼容），其余外部源按目录名排序追加。</summary>
    private static string[] ResolveOrder(LyricoSourceCatalog catalog)
    {
        var names = catalog.PluginNames;
        var result = new List<string>(names.Count);
        foreach (var p in PriorityOrder)
            if (names.Contains(p, StringComparer.OrdinalIgnoreCase) && !result.Contains(p, StringComparer.OrdinalIgnoreCase))
                result.Add(p);
        foreach (var p in names)
            if (!result.Contains(p, StringComparer.OrdinalIgnoreCase))
                result.Add(p);
        return result.ToArray();
    }

    public IReadOnlyList<string> AvailablePlugins => _catalog.PluginNames;

    private void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;
            foreach (var plugin in _order)
            {
                if (!_catalog.PluginNames.Contains(plugin, StringComparer.OrdinalIgnoreCase)) continue;
                var manifest = _catalog.GetManifest(plugin);
                if (manifest == null) continue;
                if (!manifest.Capabilities.Contains("getLyrics", StringComparer.OrdinalIgnoreCase)) continue;
                _hosts[plugin] = new LyricoScriptHost(_catalog, plugin, manifest);
            }
            _initialized = true;
        }
    }

    /// <summary>
    /// 取指定歌曲歌词：依次尝试各源，返回首个优质结果的 LrcLyrics；全部失败返回 null。
    /// </summary>
    public async Task<LrcLyrics?> GetAsync(string title, string artist, string? album,
        double durationSeconds, CancellationToken ct = default)
    {
        EnsureInitialized();
        if (_hosts.Count == 0) return null;

        foreach (var (plugin, host) in _hosts)
        {
            if (ct.IsCancellationRequested) return null;
            if (_disabled.Contains(plugin)) continue;  // 用户禁用的源跳过
            try
            {
                var candidates = await host.GetLyricsAsync(
                    title, artist, album ?? "", (long)(durationSeconds * 1000), ct).ConfigureAwait(false);
                var best = PickBest(candidates, title, durationSeconds);
                if (best != null)
                {
                    LyricoLog.Debug(plugin, $"命中歌词 lines={best.Lines.Count}");
                    return best;
                }
            }
            catch (Exception ex)
            {
                LyricoLog.Warn(plugin, $"歌词获取异常：{ex.Message}");
            }
        }
        return null;
    }

    /// <summary>从候选里选最优：歌名一致加权 + 时长相近加权，分低/纯空直接丢弃。</summary>
    private static LrcLyrics? PickBest(List<object?> candidates, string title, double durationSeconds)
    {
        LrcLyrics? best = null;
        double bestScore = -1;
        foreach (var candidate in candidates)
        {
            var lyrics = LyricoLyricsConverter.Convert(candidate);
            if (lyrics == null || lyrics.Lines.Count == 0) continue;

            double score = 1;
            var ti = lyrics.Metadata.Title?.Trim();
            if (!string.IsNullOrEmpty(ti))
            {
                var normalized = StripVersionSuffix(ti);
                if (string.Equals(normalized, title, StringComparison.OrdinalIgnoreCase))
                    score += 3;               // 歌名（去掉“伴奏/纯音乐/inst”等后缀后）一致：强命中
            }
            else
            {
                score += 0.5;
            }

            // 有效歌词行数：占位/器乐行越少、真实歌词行越多，越可能是演唱版
            var realLines = lyrics.Lines.Count(l => !IsPlaceholder(l.Text));
            if (realLines >= 10)
                score += 2 + Math.Min(2, realLines / 20.0);  // 行越多越稳，封顶约 +4
            else if (realLines <= 3)
                score -= 3;                   // 几乎全是“纯音乐/请欣赏”等占位 → 大概率非演唱版

            // 时长近似：末行时间贴近歌曲时长则加分（纯音乐/空壳通常远短于时长）
            if (durationSeconds > 0 && lyrics.Lines.Count > 0)
            {
                var lastSec = lyrics.Lines[^1].Timestamp.TotalSeconds;
                if (lastSec > 5)
                {
                    var diff = Math.Abs(durationSeconds - lastSec);
                    if (diff < 20) score += 1.5;
                }
            }

            if (score > bestScore + 0.001)
            {
                bestScore = score;
                best = lyrics;
            }
        }
        return bestScore > 0 ? best : null;
    }

    private static readonly string[] PlaceholderMarks =
        { "纯音乐", "请欣赏", "伴奏", "间奏", "器乐", "演奏", "instrumental", "music only", "热身", "pharrel" };

    private static bool IsPlaceholder(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        return PlaceholderMarks.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>去掉常见版本后缀（… - 伴奏 / （纯音乐）/ [inst] 等），便于歌名匹配。</summary>
    private static string StripVersionSuffix(string title)
    {
        var t = title.Trim().TrimEnd('）', ')', ']');
        var end = t.IndexOfAny(new[] { '（', '(', '[' });
        if (end > 0) t = t.Substring(0, end);
        return t.Trim();
    }

    /// <summary>预加载所有源（构建时可选预热，避免首次请求首请求慢）。</summary>
    public void WarmUp()
    {
        EnsureInitialized();
        foreach (var host in _hosts.Values) host.EnsureLoaded();
        SourceStatuses = _hosts.ToDictionary(
            x => x.Key,
            x => x.Value.IsLoaded ? "ok" : (x.Value.LoadError ?? "fail"));
    }

    /// <summary>刷新源集合：用户通过导入器装入/删除 Lyrico 源插件后调用，
    /// 重建目录编目并重置已加载的脚本宿主（下次请求时按新集合重新初始化）。</summary>
    public void Refresh()
    {
        lock (_initLock)
        {
            foreach (var host in _hosts.Values) host.Unload();
            _hosts.Clear();
            _catalog = new LyricoSourceCatalog();
            _order = ResolveOrder(_catalog);
            _disabled = LoadDisabled();
            _initialized = false;
            SourceStatuses = new Dictionary<string, string>();
        }
    }

    /// <summary>取已安装源的信息列表（目录名 + 显示名 + 加载状态），供管理 UI 展示。
    /// 未 WarmUp 时仅返回目录名与未加载状态。</summary>
    public IReadOnlyList<(string Dir, string Name, string Status)> GetSourceInfos()
    {
        EnsureInitialized();
        var result = new List<(string, string, string)>();
        foreach (var dir in _order)
        {
            if (!_catalog.PluginNames.Contains(dir, StringComparer.OrdinalIgnoreCase)) continue;
            var manifest = _catalog.GetManifest(dir);
            var name = manifest?.Name;
            if (string.IsNullOrWhiteSpace(name)) name = dir;
            string status;
            if (_hosts.TryGetValue(dir, out var host))
            {
                if (host.IsLoaded) status = "已加载";
                else if (host.LoadError != null) status = "加载失败";
                else status = "待加载";
            }
            else
            {
                status = "无 getLyrics 能力";
            }
            result.Add((dir, name, status));
        }
        return result;
    }

    /// <summary>删除指定源插件目录（卸载）。失败返回 null，成功返回被删目录名。</summary>
    public string? DeleteSource(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return null;
        try
        {
            var root = LyricoSourceCatalog.SourcesRoot;
            var path = Path.Combine(root, dir);
            if (!Directory.Exists(path)) return null;
            // 先在内存里卸载该源
            if (_hosts.TryGetValue(dir, out var host)) { host.Unload(); _hosts.Remove(dir); }
            Directory.Delete(path, recursive: true);
            return dir;
        }
        catch { return null; }
    }

    // ── 配置访问（供配置 UI 用，不依赖 JS 引擎加载）──

    /// <summary>测试单个源：直接调用该源的 getLyrics，返回解析后的歌词（供测试页验证）。
    /// 不受 _disabled 影响（测试总是执行）。</summary>
    public async Task<LrcLyrics?> TestSourceAsync(string dir, string title, string artist,
        string? album, double durationSeconds, CancellationToken ct = default)
    {
        EnsureInitialized();
        if (!_hosts.TryGetValue(dir, out var host)) return null;
        try
        {
            var candidates = await host.GetLyricsAsync(
                title, artist, album ?? "", (long)(durationSeconds * 1000), ct).ConfigureAwait(false);
            return PickBest(candidates, title, durationSeconds);
        }
        catch (Exception ex)
        {
            LyricoLog.Warn(dir, $"测试取词异常：{ex.Message}");
            return null;
        }
    }

    /// <summary>取某源插件的 manifest（含 ConfigFields，供配置页渲染表单）。</summary>
    public LyricoManifest? GetManifest(string dir)
    {
        EnsureInitialized();
        return _catalog.GetManifest(dir);
    }

    /// <summary>取某源插件的配置存储（读/写用户填写的配置值）。
    /// UI 保存后应调用 <see cref="Refresh"/> 使运行中的脚本宿主重载配置。</summary>
    public LyricoSourceConfigStore GetConfigStore(string dir)
        => new LyricoSourceConfigStore(dir);

    // ── 源启停（临时禁用某源，不卸载）──

    private static readonly string DisabledFile = Path.Combine(
        LyricoSourceCatalog.SourcesRoot, ".config", "disabled_sources.json");

    /// <summary>源是否启用（不在禁用集合中视为启用）。</summary>
    public bool IsSourceEnabled(string dir)
        => !string.IsNullOrEmpty(dir) && !_disabled.Contains(dir);

    /// <summary>启用/禁用源（立即持久化）。禁用的源在 GetAsync 中被跳过。</summary>
    public void SetSourceEnabled(string dir, bool enabled)
    {
        if (string.IsNullOrEmpty(dir)) return;
        var changed = enabled ? _disabled.Remove(dir) : _disabled.Add(dir);
        if (changed) SaveDisabled();
    }

    private static HashSet<string> LoadDisabled()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(DisabledFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(DisabledFile));
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (var e in doc.RootElement.EnumerateArray())
                        if (e.ValueKind == JsonValueKind.String) set.Add(e.GetString() ?? "");
            }
        }
        catch { }
        return set;
    }

    private void SaveDisabled()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DisabledFile)!);
            File.WriteAllText(DisabledFile,
                JsonSerializer.Serialize(_disabled.ToList()));
        }
        catch { }
    }

    public void Dispose()
    {
        foreach (var host in _hosts.Values) host.Unload();
        _hosts.Clear();
    }
}