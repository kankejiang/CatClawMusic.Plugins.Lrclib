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
            case "rawEnhancedLrc":
            case "rawMultiPersonEnhancedLrc":
                return BuildRawLrc(metadata, c.TryGetValue(type, out var v) ? v as string : null);
            case "rawTtml":
                return BuildTtml(c.TryGetValue(type, out var vt) ? vt as string : null, metadata);
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

    // ── raw LRC / Verbatim / Enhanced / MultiPerson ──
    // 统一走 LrcUnifiedParser：Plain/Verbatim/Enhanced 同一正则出词级时间戳，
    // 同时间戳多行自动分离出 罗马音/翻译 轨道；MultiPerson 退化为普通解析。

    private static LrcLyrics? BuildRawLrc(LrcMetadata metadata, string? rawLrc)
    {
        var lyrics = LrcUnifiedParser.Parse(rawLrc);
        if (lyrics == null) return null;
        lyrics.Metadata = metadata;
        return lyrics;
    }

    // ── TTML ──

    private static LrcLyrics? BuildTtml(string? ttml, LrcMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(ttml)) return null;
        try
        {
            var doc = XDocument.Parse(ttml!);
            var originals = new List<LrcLyricLine>();
            var translations = new List<LrcLyricLine>();
            var romas = new List<LrcLyricLine>();

            foreach (var p in doc.Root?.DescendantsAndSelf().Where(x => x.Name.LocalName == "p") ?? Enumerable.Empty<XElement>())
            {
                var startMs = (long)(ParseTtmlTime(Attr(p, "begin")) ?? -1);
                var endMs = (long)(ParseTtmlTime(Attr(p, "end")) ?? -1);
                if (startMs < 0 && endMs < 0) continue;
                if (startMs < 0) startMs = endMs;
                if (endMs < 0) endMs = startMs + 3000;

                var parsed = ParsePContent(p, startMs, endMs);
                switch (Role(p))
                {
                    case "x-translation":
                        AddIfText(translations, startMs, parsed.CombinedText);
                        break;
                    case "x-romanization":
                        AddIfText(romas, startMs, parsed.CombinedText);
                        break;
                    case "x-bg":
                        break;   // 背景人声不入主行
                    default:
                        var text = parsed.CombinedText.Trim();
                        if (text.Length == 0) break;
                        originals.Add(new LrcLyricLine
                        {
                            Timestamp = Ms(startMs),
                            Text = text,
                            WordTimestamps = parsed.Words.Count > 1 ? parsed.Words : null,
                            Role = Attr(p, "agent"),
                        });
                        AddIfText(translations, startMs, parsed.TranslationText);
                        AddIfText(romas, startMs, parsed.RomaText);
                        break;
                }
            }

            if (originals.Count == 0) return null;
            originals.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            translations.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            romas.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            return new LrcLyrics
            {
                Metadata = metadata,
                Lines = originals,
                TranslationLines = translations.Count > 0 ? translations : null,
                RomaLines = romas.Count > 0 ? romas : null,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>遍历 &lt;p&gt; 的直接子节点：带 begin 的 span 为词级数据；role span 归翻译/罗马音；纯文本与无时间 span 归原文。</summary>
    private static ParsedP ParsePContent(XElement p, long startMs, long endMs)
    {
        var words = new List<WordTimestamp>();
        var original = new System.Text.StringBuilder();
        var translation = new System.Text.StringBuilder();
        var roma = new System.Text.StringBuilder();

        foreach (var node in p.Nodes())
        {
            if (node is XText text)
            {
                original.Append(text.Value);
                continue;
            }
            if (node is not XElement el || el.Name.LocalName != "span") continue;

            switch (Role(el))
            {
                case "x-translation":
                    translation.Append(el.Value);
                    continue;
                case "x-romanization":
                    roma.Append(el.Value);
                    continue;
                case "x-bg":
                    continue;
            }

            var begin = ParseTtmlTime(Attr(el, "begin"));
            if (begin != null)
            {
                var wText = el.Value.Trim();
                if (wText.Length == 0) continue;
                var wEnd = (long)(ParseTtmlTime(Attr(el, "end")) ?? endMs);
                words.Add(new WordTimestamp
                {
                    Word = wText,
                    Start = Ms(begin.Value),
                    Duration = Ms(Math.Max(50, wEnd - begin.Value)),
                });
            }
            else
            {
                original.Append(el.Value);
            }
        }

        return new ParsedP(words, original.ToString(), translation.ToString(), roma.ToString());
    }

    private sealed record ParsedP(
        List<WordTimestamp> Words,
        string OriginalText,
        string TranslationText,
        string RomaText)
    {
        public string CombinedText => Words.Count > 0
            ? string.Concat(Words.Select(w => w.Word))
            : OriginalText;
    }

    private static void AddIfText(List<LrcLyricLine> list, long startMs, string text)
    {
        var t = text?.Trim();
        if (!string.IsNullOrEmpty(t))
            list.Add(new LrcLyricLine { Timestamp = Ms(startMs), Text = t });
    }

    /// <summary>取 ttm:role 属性（按本地名匹配，忽略命名空间前缀差异）。</summary>
    private static string? Role(XElement e) => Attr(e, "role");

    private static string? Attr(XElement e, string localName)
        => e.Attributes().FirstOrDefault(a => a.Name.LocalName == localName)?.Value;

    private static double? ParseTtmlTime(string? t)
    {
        if (string.IsNullOrEmpty(t)) return null;
        var m = Regex.Match(t, @"^(?:(\d+):)?(\d+):(\d+)(?:[.,](\d{1,3}))?$");
        if (!m.Success)
        {
            // 帧率形式 00:00:12:15 —— 少见，忽略
            return null;
        }
        var h = m.Groups[1].Success ? double.Parse(m.Groups[1].Value) : 0;
        var mm = double.Parse(m.Groups[2].Value);
        var ss = double.Parse(m.Groups[3].Value);
        var frac = m.Groups[4].Success ? double.Parse(m.Groups[4].Value) / Math.Pow(10, m.Groups[4].Value.Length) : 0;
        return h * 3600_000 + mm * 60_000 + ss * 1000 + frac * 1000;
    }

    // ── helpers ──

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