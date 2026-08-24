using System.Collections;
using System.Reflection;
using CatClawMusic.Plugins.Lrclib.Lyrico;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("== Lyrico 端到端测试 ==");

using var hub = new LyricoLyricsHub();
Console.WriteLine("可用源插件: " + string.Join(", ", hub.AvailablePlugins));

string title = "青花瓷";
string artist = "周杰伦";
string album = "我很忙";

try
{
    var lyrics = await hub.GetAsync(title, artist, album, 242);
    if (lyrics == null)
    {
        Console.WriteLine("结果: 未命中歌词 (低质量被过滤 / 全部失败)");
    }
    else
    {
        Console.WriteLine($"结果: 命中歌词! 行数={lyrics.Lines.Count}");
        Console.WriteLine($"元数据: {lyrics.Metadata?.Title} / {lyrics.Metadata?.Artist} / {lyrics.Metadata?.Album}");
        foreach (var line in lyrics.Lines.Take(10))
            Console.WriteLine($"  [{line.Timestamp:mm\\:ss\\.fff}] {line.Text}");
    }
}
catch (Exception ex)
{
    Console.WriteLine("GetAsync 异常: " + ex.Message);
}
Console.WriteLine("== 完成 ==");