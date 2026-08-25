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

    private readonly LyricoSourceCatalog _catalog;
    private readonly Dictionary<string, LyricoScriptHost> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _initLock = new();
    private bool _initialized;
    private readonly string[] _order;

    /// <summary>各源是否已加载（调试/诊断用）。</summary>
    public IReadOnlyDictionary<string, string> SourceStatuses { get; private set; } =
        new Dictionary<string, string>();

    public LyricoLyricsHub()
    {
        _catalog = new LyricoSourceCatalog();
        _order = ResolveOrder(_catalog);
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

    public void Dispose()
    {
        foreach (var host in _hosts.Values) host.Unload();
        _hosts.Clear();
    }
}