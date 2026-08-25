using System.Collections.Concurrent;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Plugins.Lrclib.Lyrico;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// LRCLIB 在线歌词插件：为本地/远程歌曲补齐歌词。
/// <para>
/// 实现 <see cref="ILyricsProviderPlugin"/>，宿主 LyricsService 的歌词兜底链已接线
/// （同名 .lrc、内嵌歌词都找不到时才调用本插件），装上启用即生效，零宿主改动。
/// </para>
/// <para>
/// 同时实现 <see cref="IViewContributorPlugin"/>：向宿主贡献一个「歌词匹配」入口页，
/// 用户可手动搜索 LRCLIB 候选并指定某首歌使用哪份歌词（覆盖记录持久化本地）。
/// </para>
/// <para>
/// 数据源：https://lrclib.net（开源、免费、无 API Key），按 歌名/艺人/时长 匹配同步歌词。
/// 本插件不内置任何 JS 歌词源引擎，数据源经宿主插件机制外置，保持职责单一。
/// </para>
/// </summary>
public class LrclibLyricsPlugin : ILyricsProviderPlugin, IViewContributorPlugin
{
    private readonly LrclibApiClient _client = new();
    private readonly OverrideStore _overrideStore = new();

    /// <summary>Lyrico 外部 JS 歌词源宿主（用户放入 Plugin/LyricoSources/ 的源插件），作为 LRCLIB 的兜底源。</summary>
    private readonly LyricoLyricsHub _lyrico = new();

    /// <summary>内存缓存（LRCLIB 限流 50 次/分钟/IP，重复播放/换页应命中缓存）</summary>
    private readonly ConcurrentDictionary<string, LrcLyrics?> _cache = new();
    private const int MaxCacheEntries = 300;

    /// <summary>时长差异超过该阈值（秒）时认为不是同一首歌，拒绝返回</summary>
    private const double MaxDurationDiffSeconds = 15;

    public string PluginId => "lrclib";
    public string Name => "LRCLIB 在线歌词";
    public string Version => "1.3.0";
    public string Author => "CatClawMusic";
    public string Description => "LRCLIB 开放歌词库 + 外部 Lyrico JS 源兜底（用户放入 Plugin/LyricoSources/ 的源插件）；提供 Lyrico 音乐库/手动匹配入口页";
    public List<string> Capabilities => new() { "lyrics" };

    public bool IsAvailable => true;

    // ── IViewContributorPlugin：发现页「Lyrico」入口（音乐库主框架）──

    /// <summary>发现页入口显示标题</summary>
    public string EntryTitle => "Lyrico";

    /// <summary>发现页入口图标（Emoji）</summary>
    public string EntryIcon => "🎵";

    /// <summary>
    /// 创建插件主入口页（Lyrico 风格音乐库主页）。
    /// 宿主在用户点击入口时调用：注入宿主 <see cref="IServiceProvider"/> 到 <see cref="PluginHost"/>，
    /// 供插件所有页面解析宿主服务（音乐库 / 音频文件读写 / 播放等）。
    /// </summary>
    public object CreateEntryPage(IServiceProvider services)
    {
        PluginHost.Services = services;

        var library = PluginHost.Library;
        if (library == null)
        {
            // 宿主未提供音乐库服务：给出友好提示而非空白页
            return new ContentPage
            {
                Title = "Lyrico",
                BackgroundColor = ThemeHelper.Color("WindowBackgroundColor", "#1A1838"),
                Content = new Label
                {
                    Text = "宿主未提供音乐库服务（IMusicLibraryService）",
                    TextColor = ThemeHelper.Color("TextSecondaryColor", "#C2C6E4"),
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                },
            };
        }

        return new MusicLibraryPage(new MusicLibraryViewModel(library));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task ShutdownAsync()
    {
        _cache.Clear();
        _lyrico.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取指定歌曲歌词。匹配策略（优先级从高到低）：
    /// <list type="number">
    ///   <item>手动覆盖记录：用户在「歌词匹配」入口页指定的 LRCLIB 曲目（不联网，秒返回）</item>
    ///   <item>LRCLIB 精确匹配 /get（歌名+艺人+专辑+时长）</item>
    ///   <item>搜索 /search 取分最高的候选（同步歌词优先 + 时长相近加权）</item>
    /// </list>
    /// 匹配不到或时长差异过大时返回 null，宿主继续走其余兜底。
    /// </summary>
    public async Task<LrcLyrics?> GetLyricsAsync(Song song)
    {
        if (song == null || string.IsNullOrWhiteSpace(song.Title)) return null;

        var title = song.Title.Trim();
        var artist = string.IsNullOrWhiteSpace(song.Artist) ? null : song.Artist.Trim();
        var album = string.IsNullOrWhiteSpace(song.Album) ? null : song.Album.Trim();
        var durationSeconds = NormalizeDuration(song.Duration);

        // 0) 手动覆盖：用户指定的歌词最高优先。不缓存——增删覆盖要立即生效，
        //    且覆盖命中零网络开销（仅是本地文件 mtime 比较），无需走缓存。
        var overridden = _overrideStore.Get(title, artist);
        if (overridden != null)
        {
            return ToLyrics(overridden, durationSeconds);
        }

        var cacheKey = BuildCacheKey(title, artist, album, durationSeconds);
        if (_cache.TryGetValue(cacheKey, out var cached)) return cached;

        LrcLyrics? result = null;

        // 1) 精确匹配（LRCLIB 提供 duration 参数时最可靠）
        if (result == null)
        {
            var exact = await _client.GetAsync(title, artist, album, durationSeconds);
            if (exact != null)
            {
                result = ToLyrics(exact, durationSeconds);
            }
        }

        // 2) 搜索兜底：按评分挑最佳候选
        if (result == null)
        {
            var candidates = await _client.SearchAsync(title, artist, album, durationSeconds);
            if (candidates != null && candidates.Count > 0)
            {
                var best = PickBestMatch(candidates, title, durationSeconds);
                if (best != null) result = ToLyrics(best, durationSeconds);
            }
        }

        // 3) 外部 Lyrico JS 源兜底：LRCLIB 命不中时，尝试用户放入 Plugin/LyricoSources/
        //    的源插件（网易云/QQ/酷狗/汽水/Apple 等，一次取一份 XML 元数据并择优）。
        if (result == null && _lyrico.AvailablePlugins.Count > 0)
        {
            result = await _lyrico.GetAsync(title, artist ?? "", album, durationSeconds).ConfigureAwait(false);
        }

        // 仅缓存自动匹配结果（覆盖路径已提前返回）
        AddToCache(cacheKey, result);
        return result;
    }

    /// <summary>
    /// 从候选中挑选最匹配的一首。评分规则（分高者胜）：
    /// <list type="number">
    ///   <item>歌名完全相同 +2；不区分大小写</item>
    ///   <item>时长相近：10 - 分钟级差异，越接近越高</item>
    ///   <item>有同步歌词 +1（无时间轴纯文本歌词体验差，次选）</item>
    ///   <item>纯器乐（instrumental）拒绝：LRCLIB 会收录无人声伴奏，返回空歌词毫无意义</item>
    /// </list>
    /// </summary>
    private LrclibTrack? PickBestMatch(List<LrclibTrack> candidates, string title, double durationSeconds)
    {
        LrclibTrack? best = null;
        double bestScore = -1;

        foreach (var c in candidates)
        {
            if (c.Instrumental) continue;

            double score = 0;
            if (string.Equals(c.TrackName?.Trim(), title, StringComparison.OrdinalIgnoreCase))
                score += 2;

            var diff = Math.Abs(c.Duration - durationSeconds);
            score += Math.Max(0, 10 - diff);

            if (!string.IsNullOrWhiteSpace(c.SyncedLyrics)) score += 1;

            // 候选通常已按相关度排序，但保持稳定择优：同分取先出现的
            if (score > bestScore + 0.001)
            {
                bestScore = score;
                best = c;
            }
        }

        // 防误配：已知时长但最佳候选差异过大时拒绝返回（除非歌名完全一致，说明库内数据本身时长不准）
        if (durationSeconds > 0 && best != null)
        {
            var bestDiff = Math.Abs(best.Duration - durationSeconds);
            var titleExact = string.Equals(best.TrackName?.Trim(), title, StringComparison.OrdinalIgnoreCase);
            if (bestDiff > MaxDurationDiffSeconds && !titleExact) return null;
        }

        return best;
    }

    /// <summary>把 LRCLIB 曲目转成宿主歌词模型：同步歌词优先，纯文本歌词兜底</summary>
    private static LrcLyrics? ToLyrics(LrclibTrack track, double durationSeconds)
    {
        if (!string.IsNullOrWhiteSpace(track.SyncedLyrics))
            return LrcParser.Parse(track.SyncedLyrics);

        // 纯文本：拆成整行歌词（无时间轴，宿主播放页会整首显示）
        if (!string.IsNullOrWhiteSpace(track.PlainLyrics))
        {
            var lines = track.PlainLyrics
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => new LrcLyricLine { Timestamp = TimeSpan.Zero, Text = l })
                .ToList();
            if (lines.Count == 0) return null;
            return new LrcLyrics { Lines = lines };
        }

        return null;
    }

    /// <summary>
    /// 时长归一化为秒。注意宿主 <see cref="Song.Duration"/> 存在单位不一致（注释为毫秒，
    /// 部分写入路径存的是秒）——沿用宿主播放页的防御判断：&gt;1000 视为毫秒，否则视为秒。
    /// </summary>
    private static double NormalizeDuration(int duration)
    {
        if (duration <= 0) return 0;
        return duration > 1000 ? duration / 1000.0 : duration;
    }

    private static string BuildCacheKey(string title, string? artist, string? album, double durationSeconds)
        => $"{title}|{artist}|{album}|{durationSeconds:F1}";

    private void AddToCache(string key, LrcLyrics? lyrics)
    {
        if (_cache.Count >= MaxCacheEntries)
        {
            // 简单逐出：清空重建（低频操作，避免复杂 LRU 实现）
            _cache.Clear();
        }
        _cache[key] = lyrics;
    }
}
