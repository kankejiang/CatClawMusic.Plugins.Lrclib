using System.Text;
using System.Xml.Linq;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>歌词模式（对齐 Lyrico 的 LyricFormat）。</summary>
public enum LyricMode
{
    /// <summary>逐行歌词：每行一个 [mm:ss.xxx] 时间戳（标准 LRC）</summary>
    Plain = 0,

    /// <summary>逐字歌词：每个词带 [mm:ss.xxx] 方括号时间戳</summary>
    Verbatim = 1,

    /// <summary>增强型逐字歌词：行用 [mm:ss.xxx]，词用 &lt;mm:ss.xxx&gt; 尖括号</summary>
    Enhanced = 2,

    /// <summary>TTML 歌词：XML，行 p + 词 span，带 begin/end</summary>
    TTML = 3,
}

/// <summary>
/// 把结构化 <see cref="LrcLyrics"/> 编码成四种歌词模式文本（逐行/逐字/增强/TTML）。
/// 对齐 Lyrico 的 LyricEncoder 输出格式，供预览与写入标签共用。
/// </summary>
public static class LyricModeEncoder
{
    public static string ModeName(LyricMode mode) => mode switch
    {
        LyricMode.Plain => "逐行歌词",
        LyricMode.Verbatim => "逐字歌词",
        LyricMode.Enhanced => "增强型逐字歌词",
        LyricMode.TTML => "TTML",
        _ => "逐行歌词",
    };

    /// <summary>把 LrcLyrics 按指定模式编码为歌词文本（原文行后按 Lyrico 行序跟随 罗马音、翻译 行）。</summary>
    public static string Encode(LrcLyrics? lyrics, LyricMode mode)
    {
        if (lyrics == null || lyrics.Lines.Count == 0) return "（该结果无歌词）";
        var sub = AlignSubLines(lyrics);
        return mode switch
        {
            LyricMode.Verbatim => EncodeVerbatim(lyrics, sub),
            LyricMode.Enhanced => EncodeEnhanced(lyrics, sub),
            LyricMode.TTML => EncodeTtml(lyrics, sub),
            _ => EncodePlain(lyrics, sub),
        };
    }

    /// <summary>判断歌词是否具备逐字数据（有词级时间戳才支持逐字/增强语义）。</summary>
    public static bool HasWordTimestamps(LrcLyrics? lyrics)
        => lyrics != null && lyrics.Lines.Any(l => l.WordTimestamps is { Count: > 1 });

    /// <summary>
    /// 把 RomaLines / TranslationLines 按时间戳对齐到原行（对齐 Lyrico alignSubLines：
    /// 同时间戳的多条子行按顺序取用，先到先得）。预览页与编码器共用。
    /// </summary>
    public static IReadOnlyDictionary<LrcLyricLine, (LrcLyricLine? Roma, LrcLyricLine? Trans)> AlignSubLines(
        LrcLyrics lyrics)
    {
        var map = new Dictionary<LrcLyricLine, (LrcLyricLine?, LrcLyricLine?)>();
        var romaPool = GroupByStart(lyrics.RomaLines);
        var transPool = GroupByStart(lyrics.TranslationLines);
        if (romaPool.Count == 0 && transPool.Count == 0) return map;

        foreach (var line in lyrics.Lines)
        {
            var key = (long)line.Timestamp.TotalMilliseconds;
            map[line] = (Take(romaPool, key), Take(transPool, key));
        }
        return map;
    }

    /// <summary>统计罗马音/翻译对齐行数，供预览页展示。</summary>
    public static (int Roma, int Trans) CountSubLines(LrcLyrics? lyrics)
    {
        if (lyrics == null) return (0, 0);
        var sub = AlignSubLines(lyrics);
        return (sub.Values.Count(v => v.Roma != null), sub.Values.Count(v => v.Trans != null));
    }

    private static Dictionary<long, Queue<LrcLyricLine>> GroupByStart(List<LrcLyricLine>? lines)
        => lines?.GroupBy(l => (long)l.Timestamp.TotalMilliseconds)
                 .ToDictionary(g => g.Key, g => new Queue<LrcLyricLine>(g))
             ?? new Dictionary<long, Queue<LrcLyricLine>>();

    private static LrcLyricLine? Take(Dictionary<long, Queue<LrcLyricLine>> pool, long key)
        => pool.TryGetValue(key, out var q) && q.Count > 0 ? q.Dequeue() : null;

    private static void AppendSubLines(StringBuilder sb,
        IReadOnlyDictionary<LrcLyricLine, (LrcLyricLine? Roma, LrcLyricLine? Trans)> sub, LrcLyricLine line)
    {
        if (!sub.TryGetValue(line, out var s)) return;
        if (s.Roma != null && s.Roma.Text.Length > 0)
            sb.Append('[').Append(FormatLrcTime(s.Roma.Timestamp)).Append(']').Append(s.Roma.Text).Append('\n');
        if (s.Trans != null && s.Trans.Text.Length > 0)
            sb.Append('[').Append(FormatLrcTime(s.Trans.Timestamp)).Append(']').Append(s.Trans.Text).Append('\n');
    }

    // ── 逐行：标准 LRC ──

    private static string EncodePlain(LrcLyrics lyrics,
        IReadOnlyDictionary<LrcLyricLine, (LrcLyricLine? Roma, LrcLyricLine? Trans)> sub)
    {
        var sb = new StringBuilder();
        foreach (var line in lyrics.Lines)
        {
            var t = line.Timestamp;
            sb.Append('[').Append(FormatLrcTime(t)).Append(']')
              .Append(LineText(line))
              .Append('\n');
            AppendSubLines(sb, sub, line);
        }
        return sb.ToString();
    }

    // ── 逐字：每词方括号 ──

    private static string EncodeVerbatim(LrcLyrics lyrics,
        IReadOnlyDictionary<LrcLyricLine, (LrcLyricLine? Roma, LrcLyricLine? Trans)> sub)
    {
        var sb = new StringBuilder();
        foreach (var line in lyrics.Lines)
        {
            if (line.WordTimestamps is { Count: > 1 })
            {
                var words = line.WordTimestamps;
                for (int i = 0; i < words.Count; i++)
                {
                    var w = words[i];
                    sb.Append('[').Append(FormatLrcTime(w.Start)).Append(']').Append(w.Word);
                    if (i == words.Count - 1)
                    {
                        var end = w.Duration > TimeSpan.Zero ? w.Start + w.Duration : w.Start + TimeSpan.FromMilliseconds(100);
                        sb.Append('[').Append(FormatLrcTime(end)).Append(']');
                    }
                }
            }
            else
            {
                var t = line.Timestamp;
                sb.Append('[').Append(FormatLrcTime(t)).Append(']').Append(LineText(line));
            }
            sb.Append('\n');
            AppendSubLines(sb, sub, line);
        }
        return sb.ToString();
    }

    // ── 增强：行方括号 + 词尖括号 ──

    private static string EncodeEnhanced(LrcLyrics lyrics,
        IReadOnlyDictionary<LrcLyricLine, (LrcLyricLine? Roma, LrcLyricLine? Trans)> sub)
    {
        var sb = new StringBuilder();
        foreach (var line in lyrics.Lines)
        {
            sb.Append('[').Append(FormatLrcTime(line.Timestamp)).Append(']');
            if (line.WordTimestamps is { Count: > 1 })
            {
                sb.Append(' ');
                foreach (var w in line.WordTimestamps)
                {
                    sb.Append('<').Append(FormatLrcTime(w.Start)).Append('>').Append(w.Word);
                }
                var last = line.WordTimestamps[^1];
                var end = last.Duration > TimeSpan.Zero ? last.Start + last.Duration : last.Start + TimeSpan.FromMilliseconds(100);
                sb.Append('<').Append(FormatLrcTime(end)).Append('>');
            }
            else
            {
                sb.Append(' ').Append(LineText(line));
            }
            sb.Append('\n');
            AppendSubLines(sb, sub, line);
        }
        return sb.ToString();
    }

    // ── TTML ──

    private static readonly XNamespace TtmRoleNs = "http://www.w3.org/ns/ttml#metadata";

    private static string EncodeTtml(LrcLyrics lyrics,
        IReadOnlyDictionary<LrcLyricLine, (LrcLyricLine? Roma, LrcLyricLine? Trans)> sub)
    {
        var root = new XElement("tt",
            new XAttribute(XNamespace.Xml + "lang", "en"),
            new XElement("body",
                new XAttribute(XNamespace.Xml + "lang", "en")));
        var body = root.Element("body")!;

        foreach (var line in lyrics.Lines)
        {
            var start = line.Timestamp;
            var end = NextLineStart(lyrics, line) > start
                ? NextLineStart(lyrics, line)
                : start + TimeSpan.FromMilliseconds(3000);

            var p = new XElement("p",
                new XAttribute("begin", FormatTtmlTime(start)),
                new XAttribute("end", FormatTtmlTime(end)));

            if (line.WordTimestamps is { Count: > 1 })
            {
                foreach (var w in line.WordTimestamps)
                {
                    var wEnd = w.Duration > TimeSpan.Zero ? w.Start + w.Duration : w.Start + TimeSpan.FromMilliseconds(300);
                    p.Add(new XElement("span",
                        new XAttribute("begin", FormatTtmlTime(w.Start)),
                        new XAttribute("end", FormatTtmlTime(wEnd)),
                        w.Word));
                }
            }
            else
            {
                p.Add(LineText(line));
            }

            if (sub.TryGetValue(line, out var s))
            {
                if (s.Roma is { Text.Length: > 0 })
                    p.Add(new XElement("span", new XAttribute(TtmRoleNs + "role", "x-romanization"), s.Roma.Text));
                if (s.Trans is { Text.Length: > 0 })
                    p.Add(new XElement("span", new XAttribute(TtmRoleNs + "role", "x-translation"), s.Trans.Text));
            }

            body.Add(p);
        }

        return $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n{root}";
    }

    // ── helpers ──

    private static TimeSpan NextLineStart(LrcLyrics lyrics, LrcLyricLine line)
    {
        var i = lyrics.Lines.IndexOf(line);
        if (i >= 0 && i + 1 < lyrics.Lines.Count)
            return lyrics.Lines[i + 1].Timestamp;
        return TimeSpan.Zero;
    }

    private static string LineText(LrcLyricLine line)
        => string.IsNullOrWhiteSpace(line.Text) ? "♪" : line.Text;

    /// <summary>LRC 时间格式：mm:ss.xxx</summary>
    public static string FormatLrcTime(TimeSpan t)
    {
        var totalMs = Math.Max(0, (int)t.TotalMilliseconds);
        var min = totalMs / 60_000;
        var sec = (totalMs % 60_000) / 1000;
        var ms = totalMs % 1000;
        return $"{min:D2}:{sec:D2}.{ms:D3}";
    }

    /// <summary>TTML 时间格式：HH:mm:ss.fff</summary>
    public static string FormatTtmlTime(TimeSpan t)
    {
        var totalMs = Math.Max(0, (int)t.TotalMilliseconds);
        var h = totalMs / 3_600_000;
        var min = (totalMs % 3_600_000) / 60_000;
        var sec = (totalMs % 60_000) / 1000;
        var ms = totalMs % 1000;
        return $"{h:D2}:{min:D2}:{sec:D2}.{ms:D3}";
    }
}