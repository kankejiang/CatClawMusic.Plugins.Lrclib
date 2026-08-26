using System.ComponentModel;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 页面级宽屏适配助手（Windows 桌面 / 手机横屏通用）。三件事：
/// <list type="number">
///   <item>内容限宽居中：窗口宽于限宽值时根内容居中收窄，表单行/列表行不被拉成一条长线；</item>
///   <item>详情页头部横竖排切换：窄屏封面上、文字下（居中）；宽屏封面左、文字右（左对齐填满余宽）；</item>
///   <item>桌面端悬停反馈：鼠标移入行/卡片轻微压暗，提示可点击（触屏端不附加）。</item>
/// </list>
/// 页面构造完成（Content 赋值后）调用一次即可，随窗口尺寸变化自动响应；
/// 限宽作用于原内容层，与 PluginNav 后续注入的返回头外层包装不冲突。
/// </summary>
internal static class WideAdapt
{
    /// <summary>表单/设置类页面内容限宽（窄列阅读舒适）</summary>
    public const double FormMaxWidth = 760;

    /// <summary>内容/列表类页面限宽（与音乐库 MaxContentWidth 一致）</summary>
    public const double ContentMaxWidth = ThemeHelper.MaxContentWidth;

    // ── 1) 内容限宽居中 ──

    /// <summary>已注册的尺寸监听（同一页面重复 Attach 时替换旧监听，支持页面重建 Content 后重挂）</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContentPage, EventHandler> _sizeHandlers = new();

    /// <summary>已注册的 Content 变更监听（页面重建 Content 后自动对新内容重新限宽）</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContentPage, PropertyChangedEventHandler> _contentHandlers = new();

    /// <summary>
    /// 给页面附加内容限宽。自动识别根结构：ScrollView 保持全宽（滚动条贴窗口边缘），
    /// 限宽其内部内容；其余根布局整体限宽。窗口不宽于限宽值时恢复填满，无副作用。
    /// 页面重建 Content 后无需重挂：每次应用时动态解析当前 Content，且监听 Content 替换事件。
    /// </summary>
    public static void Attach(ContentPage page, double maxWidth = ContentMaxWidth)
    {
        static Layout? ResolveTarget(ContentPage p) => p.Content switch
        {
            ScrollView { Content: Layout inner } => inner,
            Layout root => root,
            _ => null,
        };

        void Apply()
        {
            var target = ResolveTarget(page);
            if (target == null) return;
            var w = page.Width;
            if (w <= 0) return;
            if (w > maxWidth + 0.5)
            {
                target.WidthRequest = maxWidth;
                target.HorizontalOptions = LayoutOptions.Center;
            }
            else
            {
                target.WidthRequest = -1;
                target.HorizontalOptions = LayoutOptions.Fill;
            }
        }

        void OnSizeChanged(object? s, EventArgs e) => Apply();
        var handler = (EventHandler)OnSizeChanged;
        if (_sizeHandlers.TryGetValue(page, out var old))
            page.SizeChanged -= old;
        _sizeHandlers.AddOrUpdate(page, handler);
        page.SizeChanged += handler;

        void OnContentChanged(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ContentPage.Content)) Apply();
        }
        var contentHandler = (PropertyChangedEventHandler)OnContentChanged;
        if (_contentHandlers.TryGetValue(page, out var oldContent))
            page.PropertyChanged -= oldContent;
        _contentHandlers.AddOrUpdate(page, contentHandler);
        page.PropertyChanged += contentHandler;

        Apply();
    }

    // ── 2) 详情页头部横竖排切换 ──

    /// <summary>
    /// 详情页头部适配。header 需为 2 行 × 2 列（Auto,Auto / Auto,Star）的 Grid，
    /// 含 cover、text 两个子元素；窄屏初始排布（cover 跨2列第0行、text 跨2列第1行、居中）
    /// 由页面构建，本方法在宽屏时改为 cover 左 / text 右填充。
    /// onSwitch 在横竖排切换时回调（参数：是否宽屏），供页面微调文字对齐等细节。
    /// </summary>
    public static void AttachHeader(ContentPage page, Grid header, View cover, View text, Action<bool>? onSwitch = null)
    {
        void Apply()
        {
            var w = page.Width;
            if (w <= 0) return;
            var wide = ThemeHelper.IsWide(w);

            if (wide)
            {
                Grid.SetRow(cover, 0);
                Grid.SetColumn(cover, 0);
                Grid.SetColumnSpan(cover, 1);
                Grid.SetRow(text, 0);
                Grid.SetColumn(text, 1);
                Grid.SetColumnSpan(text, 1);
                text.HorizontalOptions = LayoutOptions.Fill;
                text.VerticalOptions = LayoutOptions.Center;
            }
            else
            {
                Grid.SetRow(cover, 0);
                Grid.SetColumn(cover, 0);
                Grid.SetColumnSpan(cover, 2);
                Grid.SetRow(text, 1);
                Grid.SetColumn(text, 0);
                Grid.SetColumnSpan(text, 2);
                text.HorizontalOptions = LayoutOptions.Center;
                text.VerticalOptions = LayoutOptions.Fill;
            }
            onSwitch?.Invoke(wide);
        }

        page.SizeChanged += (_, _) => Apply();
        Apply();
    }

    // ── 3) 桌面悬停反馈 ──

    /// <summary>
    /// Windows 桌面端悬停反馈：鼠标移入轻微压暗（透明度降低），移出恢复。
    /// 其余平台无鼠标悬停语义，不附加，避免触屏点击残留高亮。
    /// </summary>
    public static void AttachHover(View view)
    {
        if (DeviceInfo.Platform != DevicePlatform.WinUI) return;
        var rec = new PointerGestureRecognizer();
        rec.PointerEntered += (_, _) => view.Opacity = 0.72;
        rec.PointerExited += (_, _) => view.Opacity = 1;
        view.GestureRecognizers.Add(rec);
    }
}
