using System.Text;
using System.Text.RegularExpressions;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>歌词格式（对应 Lyrico <c>LyricFormat</c>）。</summary>
public enum LyricFormat
{
    /// <summary>普通 LRC：[mm:ss.mmm]正文</summary>
    PlainLrc,

    /// <summary>增强 LRC（词级时间轴）：[行开始]&lt;词开始&gt;词…&lt;行结束&gt;</summary>
    EnhancedLrc,

    /// <summary>TTML（Apple 式）：&lt;p begin end&gt;…&lt;/p&gt;</summary>
    Ttml,
}

/// <summary>
/// 歌词格式转换器（复刻 Lyrico <c>LyricsDocumentPipeline</c> + <c>LyricEncoder</c> 的核心路径）：
/// 把内嵌歌词解析为统一的行模型（含词级时间轴），再渲染为 Plain LRC / Enhanced LRC / TTML。
/// 纯 .NET、零宿主耦合；时间戳与 XML 转义复用 <see cref="LyricProcessor"/>。
/// </summary>
public static class LyricFormatConverter
{
    // LRC/Enhanced LRC 时间戳：[mm:ss.mmm] 或 <mm:ss.mmm>
    private static readonly Regex LrcTimeRegex = new(@"([<\[])(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?([>\]])", RegexOptions.Compiled);

    // LRC 元数据标签行：[ti:...]/[ar:...]/[al:...]/[offset:...]
    private static readonly Regex MetaTagRegex = new(@"^\[(ti|ar|al|offset):(.*)\]$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PlaceholderRegex = new(@"^[\s/\\\|｜·・.。…_-]*$", RegexOptions.Compiled);

    /// <summary>歌词行（含词级时间轴）。</summary>
    private sealed class Line
    {
        public long StartMs;
        public long EndMs = -1;
        public string Text = "";
        public List<Word> Words = new(); // 词级时间轴（无则记录整行）
    }

    private sealed class Word
    {
        public long StartMs;
        public long? EndMs;
        public string Text = "";
    }

    /// <summary>检测歌词格式；无法识别返回 null。</summary>
    public static LyricFormat? DetectFormat(string? lyrics)
    {
        if (string.IsNullOrWhiteSpace(lyrics)) return null;
        var head = lyrics!.TrimStart();
        if (head.StartsWith("<?xml") || head.StartsWith("<tt", StringComparison.OrdinalIgnoreCase))
            return LyricFormat.Ttml;
        // LRC 行内含词级 <mm:ss.mmm> 时间戳 → Enhanced
        if (LrcTimeRegex.Matches(lyrics).Any(m => m.Groups[1].Value == "<"))
            return LyricFormat.EnhancedLrc;
        return LyricFormat.PlainLrc;
    }

    /// <summary>
    /// 把歌词转换为目标格式。<c>removeEmptyLines</c> 去掉空/占位行，<c>removeTagLines</c> 过滤可见文本含指定关键词的行。
    /// 目标格式要求词级时间轴（Enhanced）但源无词级时间时，自动退化为普通行、不丢失歌词。
    /// </summary>
    public static string Convert(string? lyrics, LyricFormat target,
        bool removeEmptyLines = false, IEnumerable<string>? removeTagLines = null)
    {
        if (string.IsNullOrWhiteSpace(lyrics)) return lyrics ?? string.Empty;

        var keywords = removeTagLines?.Select(k => k.Trim()).Where(k => k.Length > 0).ToList() ?? [];
        var (lines, metadata) = DetectFormat(lyrics) == LyricFormat.Ttml
            ? ParseTtml(lyrics!)
            : ParseLrc(lyrics!);

        var kept = lines.Where(l =>
        {
            if (removeTagLines != null && keywords.Count > 0 &&
                keywords.Any(k => l.Text.Contains(k, StringComparison.OrdinalIgnoreCase))) return false;
            var trimmed = l.Text.Trim();
            if (removeEmptyLines && (trimmed.Length == 0 || PlaceholderRegex.IsMatch(trimmed))) return false;
            return true;
        }).ToList();

        return target switch
        {
            LyricFormat.PlainLrc => RenderLrc(kept, metadata, enhanced: false),
            LyricFormat.EnhancedLrc => RenderLrc(kept, metadata, enhanced: true),
            LyricFormat.Ttml => RenderTtml(kept),
            _ => lyrics,
        };
    }

    // ═════════════════ 解析 ═════════════════

    private static (List<Line> Lines, Dictionary<string, string> Meta) ParseLrc(string text)
    {
        var lines = new List<Line>();
        var meta = new Dictionary<string, string>();

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var tag = MetaTagRegex.Match(line);
            if (tag.Success && line.StartsWith("["))
            {
                meta[tag.Groups[1].Value.ToLowerInvariant()] = tag.Groups[2].Value.Trim();
                continue;
            }

            var matches = LrcTimeRegex.Matches(line).ToList();
            if (matches.Count == 0) continue; // 无时间戳的纯文本行在 LRC 语义下丢弃

            var entry = new Line();
            for (int i = 0; i < matches.Count; i++)
            {
                var m = matches[i];
                var start = ParseLrcMs(m);
                var mEnd = m.Index + m.Length;
                // 从该时间戳结束到下一时间戳开始之间的文本为该时间戳对应片段
                var nextPos = i + 1 < matches.Count ? matches[i + 1].Index : line.Length;
                var seg = line.Substring(mEnd, nextPos - mEnd);
                var isBracket = m.Groups[1].Value == "[";
                var isAngle = m.Groups[1].Value == "<";

                // 首 token 为 [行开始] 且其后跟空白 + <词开始> → 行开始标记
                if (i == 0 && isBracket && string.IsNullOrWhiteSpace(seg))
                {
                    entry.StartMs = start;
                    continue;
                }

                if (isBracket)
                {
                    // 片段级时间戳：整段作为一个词/行文本（无词级时间）时，作为普通行文本段
                    var segText = seg.Trim();
                    if (string.IsNullOrWhiteSpace(segText)) continue;
                    entry.Words.Add(new Word { StartMs = start, Text = segText });
                }
                else if (isAngle)
                {
                    var segText = seg.Trim();
                    if (string.IsNullOrWhiteSpace(segText)) continue;
                    var w = new Word { StartMs = start, Text = segText };
                    if (entry.Words.Count > 0) entry.Words[^1].EndMs = start;
                    entry.Words.Add(w);
                }
            }

            if (entry.Words.Count == 0) continue;

            // 无显式行开始时间戳时，取第一个词时间戳
            if (entry.Words.Any(w => w.StartMs >= 0) && entry.StartMs == 0)
            {
                var first = entry.Words.Min(w => w.StartMs);
                entry.StartMs = first < 0 ? 0 : first;
            }

            entry.EndMs = entry.Words[^1].EndMs ?? entry.Words[^1].StartMs + 500;
            entry.Text = string.Concat(entry.Words.Select(w => w.Text));
            lines.Add(entry);
        }

        return (lines, meta);
    }

    private static (List<Line> Lines, Dictionary<string, string> Meta) ParseTtml(string text)
    {
        var lines = new List<Line>();
        var meta = new Dictionary<string, string>();

        // 粗粒度提取 <p ...>...</p>：本地属性捕获 begin/end，span 捕获词级时间轴
        foreach (Match p in Regex.Matches(text, @"<p\b[^>]*>.*?</p>", RegexOptions.Singleline))
        {
            var pTag = Regex.Match(p.Value, @"^<p\b[^>]*>");
            if (!pTag.Success) continue;
            var attrs = pTag.Value;
            var start = AttrMs(attrs, "begin", "end");
            if (start == null) continue;
            var end = AttrMs(attrs, "end") ?? start.Value + 2000;
            var body = p.Value.Substring(pTag.Length, p.Value.Length - pTag.Length - "</p>".Length);

            var entry = new Line { StartMs = start.Value, EndMs = end };
            var words = new List<Word>();
            // 递归提取 span begin= end= 作为词
            var spanMatches = Regex.Matches(body, @"<span\b[^>]*>(.*?)</span>", RegexOptions.Singleline);
            if (spanMatches.Count > 0)
            {
                foreach (Match s in spanMatches)
                {
                    var sattrs = s.Value;
                    var wStart = AttrMs(sattrs, "begin", "end");
                    var wEnd = AttrMs(sattrs, "end");
                    var wtxt = StripMarkup(s.Groups[1].Value);
                    if (string.IsNullOrWhiteSpace(wtxt)) continue;
                    if (wStart == null)
                    {
                        if (words.Count > 0) words.Add(new Word { StartMs = words[^1].StartMs, Text = wtxt });
                        else words.Add(new Word { StartMs = start.Value, Text = wtxt });
                    }
                    else
                    {
                        if (words.Count > 0) words[^1].EndMs = wStart;
                        words.Add(new Word { StartMs = wStart.Value, EndMs = wEnd, Text = wtxt });
                    }
                }
            }
            else
            {
                var wtxt = StripMarkup(body);
                if (string.IsNullOrWhiteSpace(wtxt)) continue;
                words.Add(new Word { StartMs = start.Value, EndMs = end, Text = wtxt });
            }

            entry.Words = words;
            entry.Text = string.Concat(words.Select(w => w.Text));
            if (entry.Words.Count > 0) lines.Add(entry);
        }

        return (lines, meta);
    }

    private static long ParseLrcMs(Match m)
    {
        var min = int.Parse(m.Groups[2].Value);
        var sec = int.Parse(m.Groups[3].Value);
        var frac = m.Groups[4].Success && m.Groups[4].Value.Length > 0
            ? int.Parse(m.Groups[4].Value.PadRight(3, '0'))
            : 0;
        return min * 60000L + sec * 1000L + frac;
    }

    private static long? AttrMs(string tag, string name, string? also = null)
    {
        var rx = new Regex($@"{name}\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        var m = rx.Match(tag);
        if (!m.Success && also != null) m = new Regex($@"{also}\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase).Match(tag);
        if (!m.Success) return null;
        return ParseTtmlMs(m.Groups[1].Value);
    }

    private static long ParseTtmlMs(string s)
    {
        var t = s.Trim();
        var m = Regex.Match(t, @"^(\d+):(\d{2}):(\d{2})(?:\.(\d+))?$");
        if (m.Success)
        {
            var frac = m.Groups[4].Success && m.Groups[4].Value.Length > 0
                ? int.Parse(m.Groups[4].Value.PadRight(3, '0'))
                : 0;
            return (int.Parse(m.Groups[1].Value) * 3600L + int.Parse(m.Groups[2].Value) * 60L
                    + int.Parse(m.Groups[3].Value)) * 1000L + frac;
        }
        m = Regex.Match(t, @"^(\d+):(\d{2})(?:\.(\d+))?$");
        if (m.Success)
        {
            var frac = m.Groups[3].Success && m.Groups[3].Value.Length > 0
                ? int.Parse(m.Groups[3].Value.PadRight(3, '0'))
                : 0;
            return (int.Parse(m.Groups[1].Value) * 60L + int.Parse(m.Groups[2].Value)) * 1000L + frac;
        }
        return 0L;
    }

    private static string StripMarkup(string s) =>
        Regex.Replace(s, @"<[^>]+>", string.Empty).Trim();

    // ═════════════════ 渲染 ═════════════════

    private static string RenderLrc(List<Line> lines, Dictionary<string, string> meta, bool enhanced)
    {
        var sb = new StringBuilder();
        foreach (var kv in meta)
        {
            if (kv.Key is "ti" or "ar" or "al" or "offset")
                sb.Append('[').Append(kv.Key).Append(':').Append(kv.Value).Append("]\n");
        }
        lines.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
        foreach (var line in lines)
        {
            if (!enhanced || line.Words.Count <= 1)
            {
                sb.Append('[').Append(LyricProcessor.FormatTimestamp(line.StartMs)).Append(']')
                  .Append(line.Text).Append('\n');
                continue;
            }
            sb.Append('[').Append(LyricProcessor.FormatTimestamp(line.StartMs)).Append(']');
            foreach (var w in line.Words)
            {
                sb.Append('<').Append(LyricProcessor.FormatTimestamp(w.StartMs)).Append('>').Append(w.Text);
            }
            var lineEnd = line.Words[^1].EndMs ?? line.EndMs;
            if (lineEnd >= 0) sb.Append('<').Append(LyricProcessor.FormatTimestamp(lineEnd)).Append('>');
            sb.Append('\n');
        }
        return sb.ToString().Trim();
    }

    private static string RenderTtml(List<Line> lines)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        sb.Append("<tt xmlns=\"http://www.w3.org/ns/ttml\"")
          .Append(" xmlns:ttm=\"http://www.w3.org/ns/ttml#metadata\"")
          .Append(" xmlns:itunes=\"http://music.apple.com/lyric-ttml-internal\">\n");
        sb.Append("  <head/>\n  <body>\n    <div>\n");
        lines.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
        foreach (var line in lines)
        {
            var startStr = LyricProcessor.FormatTtmlTimestamp(line.StartMs);
            var endStr = LyricProcessor.FormatTtmlTimestamp(line.EndMs >= 0 ? line.EndMs : line.StartMs + 2000);
            sb.Append("      <p begin=\"").Append(startStr).Append("\" end=\"").Append(endStr).Append("\">");
            if (line.Words.Count > 1 && line.Words.Any(w => w.StartMs >= 0))
            {
                foreach (var w in line.Words)
                {
                    if (w.StartMs >= 0)
                    {
                        var wsdt = w.EndMs ?? line.EndMs;
                        sb.Append("<span begin=\"").Append(LyricProcessor.FormatTtmlTimestamp(w.StartMs))
                          .Append("\" end=\"").Append(LyricProcessor.FormatTtmlTimestamp(wsdt))
                          .Append("\">").Append(LyricProcessor.EscapeXml(w.Text)).Append("</span>");
                    }
                    else
                    {
                        sb.Append(LyricProcessor.EscapeXml(w.Text));
                    }
                }
            }
            else
            {
                sb.Append(LyricProcessor.EscapeXml(line.Text));
            }
            sb.Append("</p>\n");
        }
        sb.Append("    </div>\n  </body>\n</tt>");
        return sb.ToString();
    }
}