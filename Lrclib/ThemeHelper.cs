using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
// 本类内定义有 Label()/Color() 方法，与类型名同名；用别名指代 Maui 类型避免 CS0119
using MLabel = Microsoft.Maui.Controls.Label;
using MColor = Microsoft.Maui.Graphics.Color;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// Lyrico 风格页面通用 UI 工具：复用宿主全局主题资源（DynamicResource），缺失时回退默认色。
/// 供音乐库主框架 / 歌曲详情 / 搜索补全 / 批量整理等页面共用。
/// </summary>
internal static class ThemeHelper
{
    // ── 响应式断点（PC / 平板横屏 / 手机横屏 适配用）──
    /// <summary>宽屏断点：达到该宽度视为 PC / 横屏宽布局</summary>
    public const double WideBreakpoint = 720;

    /// <summary>超宽断点：内容需要限宽居中，避免行被拉得过长</summary>
    public const double UltraWideBreakpoint = 1280;

    /// <summary>内容区最大宽度（超宽屏居中显示）</summary>
    public const double MaxContentWidth = 1200;

    /// <summary>是否为宽屏布局（PC 窗口 / 横屏）</summary>
    public static bool IsWide(double width) => width >= WideBreakpoint;

    /// <summary>按可用宽度计算网格列数（专辑/封面网格用）</summary>
    public static int GridSpan(double width, double itemWidth = 190, int min = 2, int max = 8)
    {
        if (width <= 0) return min;
        var span = (int)Math.Floor(width / itemWidth);
        return Math.Clamp(span, min, max);
    }

    /// <summary>读取宿主主题颜色，缺失时回退默认色</summary>
    public static MColor Color(string key, string fallback)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c)
            return c;
        return MColor.FromArgb(fallback);
    }

    /// <summary>读取宿主主题颜色生成实心画刷</summary>
    public static Brush Brush(string key, string fallback)
        => new SolidColorBrush(Color(key, fallback));

    /// <summary>创建带主题色文本的 Label</summary>
    public static MLabel Label(double fontSize, FontAttributes weight, string key, string fallback, bool tail)
    {
        var label = new MLabel
        {
            FontSize = fontSize,
            FontAttributes = weight,
            LineBreakMode = tail ? LineBreakMode.TailTruncation : LineBreakMode.WordWrap,
        };
        label.SetDynamicResource(MLabel.TextColorProperty, key);
        _ = fallback;
        return label;
    }

    /// <summary>创建圆角卡片（深色半透明背景）</summary>
    public static Border Card(View content, double corner = 14, double margin = 3,
        string backgroundKey = "CardBackgroundColor", string backgroundFallback = "#1AFFFFFF")
        => new Border
        {
            Margin = new Thickness(12, margin),
            StrokeThickness = 0,
            Background = Brush(backgroundKey, backgroundFallback),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(corner) },
            Content = content,
        };

    /// <summary>封面色块占位：无封面时显示标题首字 + 主题色渐变底色</summary>
    public static Border CoverPlaceholder(string? text, double size = 48, double corner = 10)
    {
        var label = new MLabel
        {
            Text = string.IsNullOrEmpty(text) ? "♪" : text.Trim()[..1].ToUpperInvariant(),
            FontSize = size * 0.42,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        label.SetDynamicResource(MLabel.TextColorProperty, "PrimaryColor");

        return new Border
        {
            HeightRequest = size,
            WidthRequest = size,
            StrokeThickness = 0,
            Background = Brush("CardBackgroundStrongColor", "#2DFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(corner) },
            Content = label,
        };
    }

    /// <summary>按索引字母生成稳定占位色（与宿主艺术家列表同款色板）</summary>
    public static MColor PlaceholderColor(string seed)
    {
        var palettes = new[]
        {
            "#8C7BFF", "#FF7AAE", "#55D6FF", "#A78BFA",
            "#5EEAD4", "#FBBF24", "#818CF8", "#F472B6"
        };
        var h = 0;
        foreach (var ch in seed ?? "#") h = (h * 31 + ch) & 0x7FFFFFFF;
        return MColor.FromArgb(palettes[h % palettes.Length]);
    }

    /// <summary>时长格式化 mm:ss（秒）；0 或负值返回空字符串</summary>
    public static string FormatDuration(int seconds)
    {
        if (seconds <= 0) return string.Empty;
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}" : $"{ts.Minutes}:{ts.Seconds:00}";
    }
}

/// <summary>字符串为空/空白 → 可见（用于封面占位在无图时显示）</summary>
internal class EmptyToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is not string s || string.IsNullOrWhiteSpace(s);

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>字符串非空 → 可见（封面图在无路径时隐藏）</summary>
internal class HasValueToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is string s && !string.IsNullOrWhiteSpace(s);

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>本地封面路径 → ImageSource；空返回 null（配合 HasValue 转换器隐藏 Image）</summary>
internal class CoverSourceConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not string s || string.IsNullOrWhiteSpace(s)) return null;
        try { return ImageSource.FromFile(s); }
        catch { return null; }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>页面构建用的流式扩展：绑定 / Grid 行列 / 对齐</summary>
internal static class UiExt
{
    public static T Bind<T>(this T self, string path, BindableProperty property) where T : BindableObject
    {
        self.SetBinding(property, path);
        return self;
    }

    public static T At<T>(this T self, int row, int col) where T : BindableObject
    {
        Grid.SetRow(self, row);
        Grid.SetColumn(self, col);
        return self;
    }

    public static T Column<T>(this T self, int col) where T : BindableObject
    {
        Grid.SetColumn(self, col);
        return self;
    }

    public static T CenteredY<T>(this T self) where T : BindableObject
    {
        if (self is View v) v.VerticalOptions = LayoutOptions.Center;
        return self;
    }

    public static T CenterH<T>(this T self) where T : BindableObject
    {
        if (self is View v) v.HorizontalOptions = LayoutOptions.Center;
        return self;
    }
}
