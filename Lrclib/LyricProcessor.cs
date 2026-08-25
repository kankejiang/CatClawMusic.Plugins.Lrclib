using System.Text;
using System.Text.RegularExpressions;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>简繁转换模式（对应 Lyrico ConversionMode）。</summary>
public enum LyricConversionMode
{
    None,
    TraditionalToSimplified,
    SimplifiedToTraditional,
}

/// <summary>
/// 歌词处理工具（复刻 Lyrico 的 <c>LyricEncoder</c>/<c>LyricFormatter</c>/<c>LyricsTextCleanup</c>）。
/// 提供：简繁转换、时间轴偏移、去空行/过滤标签行、LRC/TTML 格式化时间戳、XML 转义。
/// 纯 .NET 实现、零宿主耦合；简繁转换使用紧凑字符表（覆盖歌词/歌名常用字），无外部依赖。
/// </summary>
public static class LyricProcessor
{
    // LRC/Enhanced LRC 时间戳： [01:23.456] 或 <01:23.456>
    private static readonly Regex LrcTimePattern = new(@"([<\[])(\d{2,}):(\d{2})\.(\d{2,3})([>\]])", RegexOptions.Compiled);

    // TTML 时间戳： begin="00:01:23.456" / end="00:01:23.456"
    private static readonly Regex TtmlTimePattern = new(@"(""begin=""|""end="")(\d{2,}):(\d{2}):(\d{2})\.(\d{2,3})("")", RegexOptions.Compiled);

    /// <summary>可视文本占位正则（LyricsTextCleanup.isBlankOrPlaceholder）</summary>
    private static readonly Regex PlaceholderRegex = new(@"^[\s/\\\|｜·・.。…_-]*$", RegexOptions.Compiled);

    // ═════════════════════ 时间戳格式化 ═════════════════════

    /// <summary>LRC 时间戳 mm:ss.SSS</summary>
    public static string FormatTimestamp(long millis)
    {
        var safe = Math.Max(0, millis);
        return string.Format("{0:00}:{1:00}.{2:000}", safe / 60000, (safe % 60000) / 1000, safe % 1000);
    }

    /// <summary>TTML 时间戳 HH:mm:ss.SSS</summary>
    public static string FormatTtmlTimestamp(long millis)
    {
        var safe = Math.Max(0, millis);
        return string.Format("{0:00}:{1:00}:{2:00}.{3:000}",
            safe / 3600000, (safe % 3600000) / 60000, (safe % 60000) / 1000, safe % 1000);
    }

    // ═════════════════════ 时间轴偏移 ═════════════════════

    /// <summary>
    /// 对歌词全文做时间偏移（支持 LRC / Enhanced LRC / Verbatim / TTML 的时间戳）。
    /// 偏移单位毫秒：正数延后，负数提前；结果不小于 0。
    /// </summary>
    public static string ShiftOffset(string? lyrics, int offsetMs)
    {
        if (offsetMs == 0 || string.IsNullOrWhiteSpace(lyrics)) return lyrics ?? string.Empty;

        var result = lyrics!;

        // LRC：保持括号类型，标准化为 3 位毫秒
        result = LrcTimePattern.Replace(result, m =>
        {
            var prefix = m.Groups[1].Value;
            var min = long.Parse(m.Groups[2].Value);
            var sec = long.Parse(m.Groups[3].Value);
            var ms = PadMilliseconds(m.Groups[4].Value);
            var suffix = m.Groups[5].Value;
            var total = Math.Max(0, (min * 60 + sec) * 1000 + ms + offsetMs);
            return $"{prefix}{FormatTimestamp(total)}{suffix}";
        });

        // TTML
        result = TtmlTimePattern.Replace(result, m =>
        {
            var prefix = m.Groups[1].Value;
            var hr = long.Parse(m.Groups[2].Value);
            var min = long.Parse(m.Groups[3].Value);
            var sec = long.Parse(m.Groups[4].Value);
            var ms = PadMilliseconds(m.Groups[5].Value);
            var suffix = m.Groups[6].Value;
            var total = Math.Max(0, (hr * 3600 + min * 60 + sec) * 1000 + ms + offsetMs);
            return $"{prefix}{FormatTtmlTimestamp(total)}{suffix}";
        });

        return result;
    }

    private static long PadMilliseconds(string msStr)
    {
        var s = msStr.PadRight(3, '0');
        return long.Parse(s.Length > 3 ? s.Substring(0, 3) : s);
    }

    // ═════════════════════ 清洗（去空行 / 过滤标签行） ═════════════════════

    /// <summary>
    /// 清洗歌词：按行过滤空行/占位行与包含指定关键词的标签行（同 LyricsTextCleanup）。
    /// <c>tagKeywords</c> 例如 ["[ti:", "[ar:", "[al:"...]。
    /// </summary>
    public static string Cleanup(string? raw, bool removeEmptyLines, IEnumerable<string> tagKeywords)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw ?? string.Empty;

        var keywords = tagKeywords.Select(k => k.Trim()).Where(k => k.Length > 0).ToList();

        return string.Join('\n', raw!.Replace("\r\n", "\n").Split('\n')
            .Where(line =>
            {
                var visible = VisibleLineText(line);
                var trimmed = visible.Trim();
                var removeEmpty = removeEmptyLines && (trimmed.Length == 0 || PlaceholderRegex.IsMatch(trimmed));
                var removeTag = keywords.Any(k =>
                    line.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                    visible.Contains(k, StringComparison.OrdinalIgnoreCase));
                return !(removeEmpty || removeTag);
            }))
            .Trim();
    }

    private static string VisibleLineText(string line) =>
        LrcTimePattern.Replace(Regex.Replace(line, @"<[^>]+>", string.Empty), string.Empty);

    // ═════════════════════ 简繁转换 ═════════════════════

    /// <summary>转换歌词全文：仅转换时间戳与标签标记之外的正文（对应 Lyrico convertLyricsText）。</summary>
    public static string ConvertLyrics(string? lyrics, LyricConversionMode mode)
    {
        if (mode == LyricConversionMode.None || string.IsNullOrWhiteSpace(lyrics)) return lyrics ?? string.Empty;

        // 切分出时间戳 token（LRC/Enhanced 与 TTML），其余片段做转换
        var tokenPattern = new Regex(@"([<\[]\d{2,}:\d{2}\.\d{2,3}[>\]])|(""begin=""\d{2,}:\d{2}:\d{2}\.\d{2,3}"")|(""end=""\d{2,}:\d{2}:\d{2}\.\d{2,3}"")", RegexOptions.Compiled);
        var sb = new StringBuilder();
        var pos = 0;
        foreach (Match m in tokenPattern.Matches(lyrics!))
        {
            if (m.Index > pos) sb.Append(ConvertText(lyrics.Substring(pos, m.Index - pos), mode));
            sb.Append(m.Value); // 时间戳原样保留
            pos = m.Index + m.Length;
        }
        if (pos < lyrics!.Length) sb.Append(ConvertText(lyrics.Substring(pos), mode));
        return sb.ToString();
    }

    /// <summary>转换单个文本段。</summary>
    public static string ConvertText(string text, LyricConversionMode mode)
    {
        if (string.IsNullOrEmpty(text) || mode == LyricConversionMode.None) return text;
        var map = mode == LyricConversionMode.TraditionalToSimplified ? _toSimplified : _toTraditional;
        var sb = new StringBuilder(text.Length);
        foreach (var c in text) sb.Append(map.TryGetValue(c, out var r) ? r : c);
        return sb.ToString();
    }

    // ═════════════════════ XML 转义（TTML 用） ═════════════════════

    public static string EscapeXml(string text) => text
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\"", "&quot;").Replace("'", "&apos;");

    public static string UnescapeXml(string text) => text
        .Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"")
        .Replace("&apos;", "'").Replace("&amp;", "&");

    // ═════════════════════ 简繁字符映射 ═════════════════════

    // 简体 → 繁体（字符一一对应）。覆盖歌词/歌名/艺人常用字，表外字原样保留。
    private const string Simplified = "爱碍罢摆办帮宝报备贝笔边变标别宾补才残仓厕层叉产尝车彻陈尘衬称诚驰迟齿虫丑处触传窗床词赐匆从丛错达带单旦弹当党刀导灯邓敌抵点电店垫钓调跌东动栋斗独读赌断兑队对吨坠夺朵饿儿尔发罚阀饭范飞废费奋愤丰风凤奉夫福妇复该概干赶刚钢杠岗告哥各给跟更工共沟钩构购估股故顾观馆惯广归规龟贵滚锅过哈害汉航毫好号喝合河贺黑很洪红轰后候湖互华化画话怀欢还环换慌黄皇灰挥辉回会混活伙或货击机鸡积极集几计记纪技际剂济加家价坚间监尖键见建件讲降焦教阶接街节洁结解姐戒届今金津仅紧尽进禁经精景警净径竞敬境究就举剧据拒具聚卷决绝军君俊开看科颗壳克刻客课肯空孔控口扣库块亏困扩阔垃拉啦来兰拦栏蓝览懒烂郎浪劳老乐累类离李里礼丽厉立丽励利例连联脸练炼凉梁两辆亮谅疗僚了列烈邻林临淋灵零领另流留刘柳六龙楼漏露陆录路吕旅虑论罗络落妈码马买麦卖满慢忙贸么没每美门们梦迷密棉免面民名命模末莫某母亩木目拿哪那纳乃奶南难脑闹内能尼你年念娘鸟宁牛农浓弄努女暖欧偶爬怕排牌派盼旁跑赔佩朋皮篇片票漂拼品平评凭瓶坡婆破扑铺普七妻期齐其奇骑启起气汽器千迁铅前钱欠强抢桥亲轻清请庆穷秋求区曲取去圈权全劝缺确群然让热人认任扔仍日荣容溶肉如入软瑞锐若洒赛三散色沙纱山闪善伤商赏上少绍设社谁申深神沈审生声省圣胜师施十石时识实拾食士世市示式事饰逝势是适收手首受兽书叔舒输熟属术束树数双水税睡顺说司丝私思斯死寺送搜苏诉素虽随岁碎孙所锁他她太态谈汤唐堂逃桃陶讨特疼梯提题体天田填条铁厅听停同铜统痛头投透突图途土吐团推退吞托脱弯玩晚万王网往忘望危威微为围唯维伟伪尾委卫未位谓温文闻问无吾午伍武舞物误悟雾西惜习席洗喜细系虾峡下夏先仙鲜闲显险县现线限馅乡相香详响想向项象像消销小笑些写谢心新兴星行形省醒幸性姓兄雄休修须需许续轩悬选学雪血寻询训讯迅压呀芽雅亚咽言颜厌验扬羊阳洋养样腰摇遥要药爷野业叶夜一医依仪宜已以亿义艺忆议亦异易益谊意因阴音银引饮隐应英营赢影映拥永勇用优优由油游友有又右幼于余鱼娱雨语预遇元员原圆远院愿约月悦越云允运杂灾栽载再在咱赞脏早造责择泽贼怎增赠扎诈摘宅窄沾粘展占战站章张帐账招找赵照折者这针真阵争怔整正证郑政之支汁织知执直植值职纸只指志制治质致智置中忠终钟肿种众州舟周洲皱珠诸竹逐主住助注祝专砖转庄装壮状追准桌卓姿资子字自综总纵走奏租足族组祖钻最罪尊昨左作业坐座" +
            "呗叽哝咂嘱嘌啭吓哟";
    private const string Traditional = "愛礙罷擺辦幫寶報備貝筆邊變標別賓補才殘倉廁層叉產嘗車徹陳塵襯稱誠馳遲齒蟲丑處觸傳窗床詞賜匆從叢錯達帶單旦彈當黨刀導燈鄧敵抵點電店墊釣調跌東動棟鬥獨讀賭斷兌隊對噸墜奪朵餓兒爾發罰閥飯範飛廢費奮憤豐風鳳奉夫福婦復該概幹趕剛鋼杠崗告哥各給跟更工共溝鉤構購估股故顧觀館慣廣歸規龜貴滾鍋過哈害漢航毫好號喝合河賀黑很洪紅轟後候湖互華化畫話懷歡還環換慌黃皇灰揮輝回會混活夥或貨擊機雞積極集幾計記紀技際劑濟加家價堅間監尖鍵見建件講降焦教階接街節潔結解姐戒屆今金津僅緊盡進禁經精景警淨徑競敬境究就舉劇據拒具聚卷決絕軍君俊開看科顆殼克刻客課肯空孔控口扣庫塊虧困擴闊垃拉啦來蘭攔欄藍覽懶爛郎浪勞老樂累類離李裡禮麗厲立麗勵利例連聯臉練煉涼梁兩輛亮諒療僚了列烈鄰林臨淋靈零領另流留劉柳六龍樓漏露陸錄路呂旅慮論羅絡落媽碼馬買麥賣滿慢忙貿麼沒每美門們夢迷密棉免面民名命模末莫某母畝木目拿哪那納乃奶南難腦鬧內能尼你年念娘鳥寧牛農濃弄努女暖歐偶爬怕排牌派盼旁跑賠佩朋皮篇片票漂拼品平評憑瓶坡婆破撲鋪普七妻期齊其奇騎啟起氣汽器千遷鉛前錢欠強搶橋親輕清請慶窮秋求區曲取去圈權全勸缺確群然讓熱人認任扔仍日榮容溶肉如入軟瑞銳若灑賽三散色沙紗山閃善傷商賞上少紹設社誰申深神沈審生聲省聖勝師施十石時識實拾食士世市示式事飾逝勢是適收手首受獸書叔舒輸熟屬術束樹數雙水稅睡順說司絲私思斯死寺送搜蘇訴素雖隨歲碎孫所鎖他她太態談湯唐堂逃桃陶討特疼梯提題體天田填條鐵廳聽停同銅統痛頭投透突圖途土吐團推退吞托脫彎玩晚萬王網往忘望危威微為圍唯維偉偽尾委衛未位謂溫文聞問無吾午伍武舞物誤悟霧西惜習席洗喜細系蝦峽下夏先仙鮮閒顯險縣現線限餡鄉相香詳響想向項象像消銷小笑些寫謝心新興星行形省醒幸性姓兄雄休修須需許續軒懸選學雪血尋詢訓訊迅壓呀芽雅亞咽言顏厭驗揚羊陽洋養樣腰搖遙要藥爺野業葉夜一醫依儀宜已以億義藝憶議亦異易益誼意因陰音銀引飲隱應英營贏影映擁永勇用優優由油遊友有又右幼於余魚娛雨語預遇元員原圓遠院願約月悅越雲允運雜災栽載再在咱贊臟早造責擇澤賊怎增贈扎詐摘宅窄沾粘展占戰站章張帳賬招找趙照折者這針真陣爭怔整正證鄭政之支汁織知執直植值職紙只指志制治質致智置中忠終鐘腫種眾州舟周洲皺珠諸竹逐主住助注祝專磚轉莊裝壯狀追準桌卓姿資子字自綜總縱走奏租足族組祖鑽最罪尊昨左作業坐座" +
            "唄嘰噥囑嘌囀嚇喲";

    private static readonly Dictionary<char, char> _toSimplified = BuildPairMap(Simplified, Traditional);
    private static readonly Dictionary<char, char> _toTraditional = BuildPairMap(Traditional, Simplified);

    private static Dictionary<char, char> BuildPairMap(string from, string to)
    {
        var dict = new Dictionary<char, char>();
        var n = Math.Min(from.Length, to.Length);
        for (var i = 0; i < n; i++)
        {
            var f = from[i];
            var t = to[i];
            if (f == t) continue;
            dict[f] = t;
        }
        return dict;
    }
}