using System.Text.RegularExpressions;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 统一 LRC 解析器（对齐 Lyrico LrcDocumentFormat.parseLrc / separateLrcTracks）：
/// 同一正则匹配 [mm:ss.xxx] 与 &lt;mm:ss.xxx&gt; 时间戳，行内每个时间戳到下一个时间戳之间的文本即一个词。
/// Plain（单时间戳）/ Verbatim（行内多方括号）/ Enhanced（行方括号+词尖括号）由此统一获得词级数据；
/// 同一时间戳的多行再按 Lyrico 规则分离多轨道：词级行优先为原文，其余子行第一行为罗马音、其余为翻译。
/// </summary>
public static class LrcUnifiedParser
{
    private static readonly Regex TimeToken =
        new(@"([<\[])(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?([>\]])", RegexOptions.Compiled);

    private static readonly Regex MetadataTag =
        new(@"^\[[A-Za-z][A-Za-z0-9_-]*:.*\]$", RegexOptions.Compiled);

    /// <summary>解析任意 LRC 变体（Plain/Verbatim/Enhanced/多语言多行）为 LrcLyrics；无可解析行返回 null。</summary>
    public static LrcLyrics? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var lines = new List<LrcLyricLine>();
        foreach (var rawLine in raw.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || MetadataTag.IsMatch(line)) continue;
            lines.AddRange(ParseLine(line));
        }
        if (lines.Count == 0) return null;

        lines.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return SeparateTracks(lines);
    }

    /// <summary>
    /// 解析单行。经典多时间戳行（[00:12.34][00:45.67]文本，所有方括号且仅末尾有文本）
    /// 按语义展开为多行；其余情况行内每时间戳产出词级数据。
    /// </summary>
    private static List<LrcLyricLine> ParseLine(string line)
    {
        var matches = TimeToken.Matches(line);
        if (matches.Count == 0) return Empty();

        var tokens = new List<(long StartMs, int Index, int End)>();
        foreach (Match m in matches)
        {
            var open = m.Groups[1].Value[0];
            var close = m.Groups[5].Value[0];
            if ((open == '[' && close != ']') || (open == '<' && close != '>')) continue;
            tokens.Add((ParseMs(m), m.Index, m.Index + m.Length));
        }
        if (tokens.Count == 0) return Empty();

        // 词文本：本时间戳结尾 → 下一时间戳开头；尾词到行尾
        var words = new List<(string Text, long StartMs, long EndMs)>();
        var allBrackets = tokens.All(t => line[t.Index] == '[');
        for (int i = 0; i < tokens.Count; i++)
        {
            var text = i + 1 < tokens.Count
                ? line.Substring(tokens[i].End, tokens[i + 1].Index - tokens[i].End)
                : line.Substring(tokens[i].End);
            text = text.Trim();
            if (text.Length == 0) continue;   // 含增强 LRC 行首时间戳（后随尖括号）的自然跳过
            var start = tokens[i].StartMs;
            var end = i + 1 < tokens.Count ? tokens[i + 1].StartMs : start + 500;
            words.Add((text, start, end));
        }

        // 经典多时间戳行：所有 token 均为方括号且只有最后一个有文本 → 每个时间戳一行
        if (allBrackets && words.Count == 1 && words[0].StartMs == tokens[^1].StartMs)
        {
            return tokens.Select(t => new LrcLyricLine
            {
                Timestamp = TimeSpan.FromMilliseconds(t.StartMs),
                Text = words[0].Text,
            }).ToList();
        }

        var lineText = string.Concat(words.Select(w => w.Text));
        if (lineText.Length == 0) return Empty();

        return new List<LrcLyricLine>
        {
            new()
            {
                Timestamp = TimeSpan.FromMilliseconds(tokens[0].StartMs),
                Text = lineText,
                WordTimestamps = words.Count > 1
                    ? words.Select(w => new WordTimestamp
                    {
                        Word = w.Text,
                        Start = TimeSpan.FromMilliseconds(w.StartMs),
                        Duration = TimeSpan.FromMilliseconds(Math.Max(50, w.EndMs - w.StartMs)),
                    }).ToList()
                    : null,
            },
        };
    }

    /// <summary>
    /// 多轨道分离：按行起始时间分组。组内多行时，词级行（WordTimestamps&gt;1）优先作原文；
    /// 其余子行第一行为罗马音、其余为翻译；仅一个子行时视为翻译。
    /// </summary>
    private static LrcLyrics SeparateTracks(List<LrcLyricLine> lines)
    {
        var grouped = lines
            .GroupBy(l => (long)l.Timestamp.TotalMilliseconds)
            .OrderBy(g => g.Key)
            .Select(g => g.ToList())
            .ToList();
        if (grouped.All(g => g.Count == 1))
            return new LrcLyrics { Lines = lines };

        var original = new List<LrcLyricLine>();
        var roma = new List<LrcLyricLine>();
        var translation = new List<LrcLyricLine>();
        foreach (var group in grouped)
        {
            var wordLevel = group.Where(l => l.WordTimestamps is { Count: > 1 }).ToList();
            var originals = wordLevel.Count > 0 ? wordLevel : new List<LrcLyricLine> { group[0] };
            original.AddRange(originals);

            var subs = group.Where(l => !originals.Contains(l)).ToList();
            if (subs.Count >= 2)
            {
                roma.Add(subs[0]);
                translation.AddRange(subs.Skip(1));
            }
            else if (subs.Count == 1)
            {
                translation.Add(subs[0]);
            }
        }

        return new LrcLyrics
        {
            Lines = original,
            RomaLines = roma.Count > 0 ? roma : null,
            TranslationLines = translation.Count > 0 ? translation : null,
        };
    }

    private static List<LrcLyricLine> Empty() => new();

    private static long ParseMs(Match m)
    {
        var min = long.Parse(m.Groups[2].Value);
        var sec = long.Parse(m.Groups[3].Value);
        var frac = m.Groups[4].Value;
        var ms = frac.Length switch
        {
            0 => 0,
            1 => int.Parse(frac) * 100,
            2 => int.Parse(frac) * 10,
            _ => int.Parse(frac[..3]),
        };
        return (min * 60 + sec) * 1000 + ms;
    }
}
