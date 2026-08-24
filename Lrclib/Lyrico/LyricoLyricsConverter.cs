using System.Text.RegularExpressions;
using System.Xml.Linq;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Plugins.Lrclib.Lyrico;

/// <summary>
/// 把 Lyrico 源返回的歌词候选（CLR 形式）转换成宿主 <see cref="LrcLyrics"/>。
/// 支持 structured、rawPlainLrc/rawVerbatimLrc、rawEnhancedLrc、rawTtml、rawMultiPersonEnhancedLrc。
/// </summary>
public static class LyricoLyricsConverter
{
    /// <summary>把单个候选转成 LrcLyrics；不适用/无内容返回 null。</summary>
    public static LrcLyrics? Convert(object? candidate)
    {
        if (candidate is not Dictionary<string, object?> c) return null;
        var type = S(c, "type");
        var tags = c.TryGetValue("tags", out var t) && t is Dictionary<string, object?> d ? d : new Dictionary<string, object?>();
        var metadata = BuildMetadata(tags);

        switch (type)
        {
            case "structured":
                return BuildStructured(c, metadata);
            case "rawPlainLrc":
            case "rawVerbatimLrc":
                return BuildRawLrc(c, metadata, c.TryGetValue(type, out var v) ? v as string : null, enhanced: false);
            case "rawEnhancedLrc":
                return BuildRawLrc(c, metadata, c.TryGetValue(type, out var ve) ? ve as string : null, enhanced: true);
            case "rawTtml":
                return BuildTtml(c.TryGetValue(type, out var vt) ? vt as string : null, metadata);
            case "rawMultiPersonEnhancedLrc":
                return BuildMultiPerson(c.TryGetValue(type, out var vm) ? vm as string : null, metadata);
            default:
                return BuildStructured(c, metadata);
        }
    }

    private static LrcMetadata BuildMetadata(Dictionary<string, object?> tags) => new()
    {
        Title = S(tags, "ti"),
        Artist = S(tags, "ar"),
        Album = S(tags, "al"),
    };

    // ── structured ──

    private static LrcLyrics? BuildStructured(Dictionary<string, object?> c, LrcMetadata metadata)
    {
        var original = c.TryGetValue("original", out var o) && o is List<object?> ol ? ol
            : (o is System.Collections.IEnumerable ie ? Cast(ie).ToList() : new List<object?>());

        var lines = new List<LrcLyricLine>();
        foreach (var raw in original)
        {
            var line = ParseStructuredLine(raw);
            if (line != null) lines.Add(line);
        }
        if (lines.Count == 0) return null;

        lines.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        var lyrics = new LrcLyrics { Metadata = metadata, Lines = lines };

        TranslateStream(c, "translated", lyrics, isTranslation: true);
        TranslateStream(c, "romanization", lyrics, isTranslation: false);
        return lyrics.Lines.Count == 0 ? null : lyrics;
    }

    private static LrcLyricLine? ParseStructuredLine(object? raw)
    {
        if (raw is not List<object?> line || line.Count == 0) return null;
        var start = Num(line[0]);
        var content = line.Count > 2 ? line[2] : null;

        string text;
        List<WordTimestamp>? words = null;
        if (content is List<object?> wordList)
        {
            var sb = new System.Text.StringBuilder();
            var wts = new List<WordTimestamp>();
            foreach (var w in wordList)
            {
                if (w is not List<object?> wline || wline.Count == 0) continue;
                var wStart = wline.Count > 0 ? Num(wline[0]) : 0;
                var wEnd = wline.Count > 1 ? Num(wline[1]) : wStart;
                var wText = wline.Count > 2 ? S(wline, 2) : "";
                if (string.IsNullOrEmpty(wText)) { continue; }
                sb.Append(wText);
                wts.Add(new WordTimestamp { Word = wText, Start = Ms(wStart), Duration = Ms(Math.Max(0, wEnd - wStart)) });
            }
            text = sb.ToString();
            if (wts.Count > 1) words = wts;
        }
        else
        {
            text = content is string str ? str : "";
        }

        if (string.IsNullOrEmpty(text)) return null;

        var end = line.Count > 1 ? Num(line[1]) : start;
        return new LrcLyricLine
        {
            Timestamp = Ms(start),
            Text = text,
            WordTimestamps = words,
        };
    }

    private static void TranslateStream(Dictionary<string, object?> c, string key, LrcLyrics lyrics, bool isTranslation)
    {
        if (!c.TryGetValue(key, out var stream) || stream is not List<object?> streamList) return;
        var result = new List<LrcLyricLine>();
        foreach (var raw in streamList)
        {
            if (raw is not List<object?> line || line.Count < 3) continue;
            var start = Num(line[0]);
            var text = S(line, 2);
            if (string.IsNullOrEmpty(text)) continue;
            result.Add(new LrcLyricLine { Timestamp = Ms(start), Text = text });
        }
        if (result.Count == 0) return;
        result.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        if (isTranslation) lyrics.TranslationLines = result;
        else lyrics.RomaLines = result;
    }

    // ── raw LRC / Enhanced ──

    private static readonly Regex EnhancedWordTag = new(@"<(\d+):([\d.]+)>", RegexOptions.Compiled);

    private static LrcLyrics? BuildRawLrc(Dictionary<string, object?> c, LrcMetadata metadata, string? rawLrc, bool enhanced)
    {
        if (string.IsNullOrWhiteSpace(rawLrc)) return null;
        var lines = enhanced ? ParseEnhancedLrc(rawLrc) : ParsePlainLrc(rawLrc);
        if (lines.Count == 0) return null;
        return new LrcLyrics { Metadata = metadata, Lines = lines };
    }

    /// <summary>普通 LRC 解析（复用宿主 LrcParser 语义的行级解析）。</summary>
    private static List<LrcLyricLine> ParsePlainLrc(string text)
    {
        var result = new List<LrcLyricLine>();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            var matches = Regex.Matches(line, @"\[(\d{1,2}):(\d{2})(?:[.:](\d{1,3}))?\]");
            if (matches.Count == 0) continue;
            var content = line.Substring(line.IndexOf(']') + 1).Trim();
            if (string.IsNullOrEmpty(content)) continue;
            foreach (Match m in matches)
            {
                var start = (int)(int.Parse(m.Groups[1].Value) * 60_000 + int.Parse(m.Groups[2].Value) * 1000
                    + ParseFraction(m.Groups[3].Value));
                result.Add(new LrcLyricLine { Timestamp = Ms(start), Text = StripWordTags(content) });
            }
        }
        result.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return result;
    }

    private static List<LrcLyricLine> ParseEnhancedLrc(string text)
    {
        var result = new List<LrcLyricLine>();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            var m = Regex.Match(line, @"\[(\d{1,2}):(\d{2})(?:[.:](\d{1,3}))?\]");
            if (!m.Success) continue;
            var lineStart = (int)(int.Parse(m.Groups[1].Value) * 60_000 + int.Parse(m.Groups[2].Value) * 1000
                + ParseFraction(m.Groups[3].Value));
            var content = line.Substring(line.IndexOf(']') + 1);

            var words = new List<WordTimestamp>();
            var sb = new System.Text.StringBuilder();
            bool hasWord = false;
            var pos = 0;
            foreach (Match w in EnhancedWordTag.Matches(content))
            {
                hasWord = true;
                var prefix = content.Substring(pos, w.Index - pos);
                sb.Append(prefix);
                if (sb.Length > 0)
                {
                    var wordEnd = (int)(int.Parse(w.Groups[1].Value) * 60_000 + (double.TryParse(w.Groups[2].Value, out var sec) ? sec * 1000 : 0));
                    if (WordsHaveText(words) || string.IsNullOrEmpty(prefix))
                    {
                        words.Add(new WordTimestamp { Word = prefix, Start = Ms(lineStart + words.Sum(x => (long)x.Duration.TotalMilliseconds)), Duration = Ms(Math.Max(0, wordEnd - (lineStart + words.Sum(x => (long)x.Duration.TotalMilliseconds)))) });
                    }
                }
                pos = w.Index + w.Length;
            }
            var tail = content.Substring(pos);
            sb.Append(tail);
            var lineText = StripWordTags(sb.ToString());
            if (string.IsNullOrEmpty(lineText)) continue;
            result.Add(new LrcLyricLine
            {
                Timestamp = Ms(lineStart),
                Text = lineText,
                WordTimestamps = hasWord && words.Count > 1 ? (List<WordTimestamp>?)words : null,
            });
        }
        result.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return result;
    }

    private static bool WordsHaveText(List<WordTimestamp> words) => words.Any(w => !string.IsNullOrEmpty(w.Word));

    // ── TTML ──

    private static LrcLyrics? BuildTtml(string? ttml, LrcMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(ttml)) return null;
        try
        {
            var doc = XDocument.Parse(ttml!);
            var result = new List<LrcLyricLine>();
            foreach (var p in doc.Root?.DescendantsAndSelf().Where(x => x.Name.LocalName == "p") ?? Enumerable.Empty<XElement>())
            {
                var begin = p.Attributes().FirstOrDefault(a => a.Name.LocalName == "begin")?.Value;
                var end = p.Attributes().FirstOrDefault(a => a.Name.LocalName == "end")?.Value;
                var startMs = (long)(ParseTtmlTime(begin) ?? 0);
                var endMs = (long)(ParseTtmlTime(end) ?? (startMs + 3000));

                var wordWords = p.Descendants().Where(x => x.Name.LocalName == "span").ToList();
                List<WordTimestamp>? words = null;
                string text;
                if (wordWords.Count > 0)
                {
                    var wts = new List<WordTimestamp>();
                    var sb = new System.Text.StringBuilder();
                    foreach (var span in wordWords)
                    {
                        var ws = ParseTtmlTime(span.Attributes().FirstOrDefault(a => a.Name.LocalName == "begin")?.Value) ?? startMs;
                        var we = ParseTtmlTime(span.Attributes().FirstOrDefault(a => a.Name.LocalName == "end")?.Value) ?? (ws + 300);
                        var wText = span.Value;
                        if (string.IsNullOrEmpty(wText)) continue;
                        sb.Append(wText);
                        wts.Add(new WordTimestamp { Word = wText, Start = Ms(ws), Duration = Ms(Math.Max(50, we - ws)) });
                    }
                    text = sb.ToString();
                    if (wts.Count > 1) words = wts;
                }
                else
                {
                    text = p.Value;
                }
                if (string.IsNullOrWhiteSpace(text)) continue;
                result.Add(new LrcLyricLine { Timestamp = Ms(startMs), Text = text.Trim(), WordTimestamps = words });
            }
            if (result.Count == 0) return null;
            result.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            return new LrcLyrics { Metadata = metadata, Lines = result };
        }
        catch
        {
            return null;
        }
    }

    private static double? ParseTtmlTime(string? t)
    {
        if (string.IsNullOrEmpty(t)) return null;
        var m = Regex.Match(t, @"^(?:(\d+):)?(\d+):(\d+)(?:[.,](\d{1,3}))?$");
        if (!m.Success)
        {
            // 帧率形式 00:00:12:15 f=... ——少见，忽略
            return null;
        }
        var h = m.Groups[1].Success ? double.Parse(m.Groups[1].Value) : 0;
        var mm = double.Parse(m.Groups[2].Value);
        var ss = double.Parse(m.Groups[3].Value);
        var frac = m.Groups[4].Success ? double.Parse(m.Groups[4].Value) / Math.Pow(10, m.Groups[4].Value.Length) : 0;
        return h * 3600_000 + mm * 60_000 + ss * 1000 + frac * 1000;
    }

    // ── MultiPerson（尽力：按多时间戳折叠为普通行） ──

    private static LrcLyrics? BuildMultiPerson(string? raw, LrcMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        // 行含共享文本与分人时间片，这里退化为取首个时间戳 + 剥离行内子标签的整行文本
        return BuildRawLrc(new Dictionary<string, object?>(), metadata, raw, enhanced: true);
    }

    private static string StripWordTags(string text) =>
        EnhancedWordTag.Replace(text ?? "", "").Replace("<", "").Replace(">", "").Trim();

    // ── helpers ──

    private static int ParseFraction(string frac)
    {
        if (string.IsNullOrEmpty(frac)) return 0;
        return frac.Length == 1 ? int.Parse(frac) * 100 : (frac.Length == 2 ? int.Parse(frac) * 10 : int.Parse(frac));
    }

    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);
    private static double Num(object? o)
    {
        return o switch
        {
            double d => d,
            int i => i,
            long l => l,
            float f => f,
            string s when double.TryParse(s, out var d) => d,
            _ => 0,
        };
    }

    private static string S(Dictionary<string, object?> d, string key) =>
        d.TryGetValue(key, out var v) ? (v as string ?? "") : "";

    private static string S(List<object?> list, int index) =>
        index < list.Count && list[index] is string s ? s : "";

    private static IEnumerable<object?> Cast(System.Collections.IEnumerable enumerable)
    {
        foreach (var item in enumerable) yield return item;
    }
}