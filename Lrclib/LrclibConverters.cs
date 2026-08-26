using System.Globalization;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 通用值转换器集合（SearchLyrics / SearchCover 页面用）。
/// 与 ManualMatchPage 的 InverseBooleanConverter 同放本命名空间，避免重复定义。
/// </summary>

/// <summary>bool → 两个值之一（true→trueValue，false→falseValue）。用于卡片透明度等。</summary>
internal sealed class BoolToValueConverter : IValueConverter
{
    private readonly object _trueValue;
    private readonly object _falseValue;

    public BoolToValueConverter(object trueValue, object falseValue)
    {
        _trueValue = trueValue;
        _falseValue = falseValue;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? _trueValue : _falseValue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool → 两个颜色之一（true→trueColor，false→falseColor）。用于徽标着色等。</summary>
internal sealed class BoolToColorConverter : IValueConverter
{
    private readonly Color _trueColor;
    private readonly Color _falseColor;

    public BoolToColorConverter(Color trueColor, Color falseColor)
    {
        _trueColor = trueColor;
        _falseColor = falseColor;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? _trueColor : _falseColor;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>字符串非空 → true（用于控制预览可见性）。</summary>
internal sealed class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrWhiteSpace(s);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>byte[]? → ImageSource（从内存流构造）。null 返回 null（让占位图标透出）。</summary>
internal sealed class IconBytesToSourceConverter : IValueConverter
{
    public static readonly IconBytesToSourceConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] bytes || bytes.Length == 0) return null;
        return ImageSource.FromStream(_ => Task.FromResult<Stream>(new MemoryStream(bytes)));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
