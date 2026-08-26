using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Plugins.Lrclib.Controls;

/// <summary>
/// 底部弹出面板（Bottom Sheet），复刻宿主 CatClawMusic.Maui.AppBottomSheet 的成熟实现：
/// 固定 0.8 屏高 + VerticalOptions=End 贴底，先设好尺寸再置可见（嵌入式宿主中运行时改高度测量不可靠），
/// 动画直写原生视图 TranslationY（MAUI 层映射可能归零但原生视图停留在初始位移），
/// 遮罩点击关闭。纯 code-only，便于插件各页面复用。
/// </summary>
public sealed class AppBottomSheet : ContentView
{
    private readonly Grid _root;
    private readonly BoxView _mask;
    private readonly Border _sheetCard;
    private readonly Grid _sheetGrid;
    private readonly ScrollView _scroll;
    private readonly VerticalStackLayout _content;

    private bool _isOpen;

    /// <summary>是否允许点击遮罩关闭（默认 true）。</summary>
    public bool CloseOnMaskTapped { get; set; } = true;

    /// <summary>内容高度占屏幕比例（默认 0.8）。</summary>
    public double HeightRatio { get; set; } = 0.8;

    public AppBottomSheet()
    {
        _content = new VerticalStackLayout { Spacing = 0 };

        _scroll = new ScrollView
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = _content,
        };

        _sheetGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
        };

        // 抓握区：顶部 32dp 透明拖拽条，内嵌半透明圆角小条
        var gripBar = new BoxView
        {
            WidthRequest = 36,
            HeightRequest = 4,
            CornerRadius = 2,
            Color = Color.FromArgb("#50000000"),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        var grip = new Grid
        {
            HeightRequest = 32,
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Start,
            Children = { gripBar },
        };
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnGripPan;
        grip.GestureRecognizers.Add(pan);
        _sheetGrid.Add(grip, 0, 0);
        _sheetGrid.Add(_scroll, 0, 1);

        _sheetCard = new Border
        {
            Background = ThemeHelper.Brush("CardBackgroundStrongColor", "#FF2A254E"),
            Stroke = ThemeHelper.Brush("GlassStrokeStrongColor", "#33FFFFFF"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(22, 22, 0, 0) },
            Padding = new Thickness(16, 10, 16, 16),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.End,
            Margin = new Thickness(8, 0, 8, 8),
            TranslationY = 600,
            Opacity = 0,
            Content = _sheetGrid,
        };

        _mask = new BoxView
        {
            Color = Color.FromArgb("#66000000"),
            Opacity = 0,
        };
        var maskTap = new TapGestureRecognizer();
        maskTap.Tapped += (_, _) => { if (CloseOnMaskTapped) _ = CloseAsync(); };
        _mask.GestureRecognizers.Add(maskTap);

        _root = new Grid { BackgroundColor = Colors.Transparent };
        _root.Add(_mask, 0, 0);
        _root.Add(_sheetCard, 0, 0);

        Content = _root;
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        IsVisible = false;
        Opacity = 0;
    }

    /// <summary>清空并追加内容，随后从底部弹出。</summary>
    public void AddContent(View view) => _content.Children.Add(view);

    public void ClearContent() => _content.Children.Clear();

    /// <summary>打开抽屉。先定好卡片尺寸再置可见，避免塌回内容高度。</summary>
    public void Open()
    {
        if (_isOpen) return;
        _isOpen = true;

        InputTransparent = false;
        _mask.Opacity = 0;
        _sheetCard.Opacity = 0;
        _sheetCard.TranslationY = 600;

        // 固定 0.8 屏高 + End 贴底（宿主同款手法）
        var screenH = ResolveScreenHeight();
        var sheetH = screenH * HeightRatio;
        _sheetCard.HeightRequest = sheetH;
        _sheetGrid.HeightRequest = sheetH;
        _scroll.ClearValue(HeightRequestProperty);
        _scroll.ClearValue(MaximumHeightRequestProperty);

        // 尺寸就绪后再置可见：首次测量即带正确高度
        IsVisible = true;
        Opacity = 1;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            _ = _mask.FadeTo(1, 200, Easing.CubicOut);
            await AnimateCardTranslationYAsync(600, 0, 300);
            SetCardTranslationY(0);
        });
    }

    public async Task CloseAsync()
    {
        if (!_isOpen) return;
        _isOpen = false;

        try
        {
            _ = _mask.FadeTo(0, 180, Easing.CubicIn);
            await AnimateCardTranslationYAsync(_sheetCard.TranslationY, 600, 200);
            await _sheetCard.FadeTo(0, 180, Easing.CubicIn);

            Opacity = 0;
            IsVisible = false;
            InputTransparent = true;
            Closed?.Invoke(this, EventArgs.Empty);
        }
        catch { /* 关闭异常不崩溃 */ }
    }

    public event EventHandler? Closed;

    private void OnGripPan(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStartY = e.TotalY;
                _panStartTy = _sheetCard.TranslationY;
                break;
            case GestureStatus.Running:
            {
                var dy = e.TotalY - _panStartY;
                if (dy > 0) SetCardTranslationY(_panStartTy + dy);
                break;
            }
            case GestureStatus.Completed:
            {
                if (_sheetCard.TranslationY > ResolveScreenHeight() * 0.25)
                    _ = CloseAsync();
                else
                    _ = AnimateCardTranslationYAsync(_sheetCard.TranslationY, 0, 180);
                break;
            }
        }
    }

    private double _panStartY;
    private double _panStartTy;

    /// <summary>直写卡片位移。MAUI 属性与原生视图双写：嵌入式宿主中脚本归零但原生停留。</summary>
    private void SetCardTranslationY(double dp)
    {
        _sheetCard.TranslationY = dp;
#if ANDROID
        try
        {
            if (_sheetCard.Handler?.PlatformView is global::Android.Views.View nv)
                nv.TranslationY = (float)(dp * nv.Resources!.DisplayMetrics!.Density);
        }
        catch { }
#endif
    }

    private async Task AnimateCardTranslationYAsync(double from, double to, uint durationMs)
    {
        const int frameMs = 16;
        for (var t = 0; t < durationMs; t += frameMs)
        {
            await Task.Delay(frameMs);
            var p = Math.Min(1.0, (t + frameMs) / (double)durationMs);
            var eased = 1 - Math.Pow(1 - p, 3);
            SetCardTranslationY(from + (to - from) * eased);
        }
        SetCardTranslationY(to);
    }

    /// <summary>解析可用屏幕高度（同宿主）：原生窗口尺寸优先，否则页高，最后回退显示尺寸。</summary>
    private double ResolveScreenHeight()
    {
#if ANDROID
        try
        {
            var act = Platform.CurrentActivity;
            var bounds = act?.Window?.WindowManager?.CurrentWindowMetrics?.Bounds;
            if (bounds is { } b && b.Height() > 0)
            {
                var d = act!.Resources!.DisplayMetrics!.Density;
                if (d > 0) return b.Height() / d;
            }
        }
        catch { }
#endif
        Element? node = Parent;
        while (node != null)
        {
            if (node is Page page && page.Height > 0) return page.Height;
            node = node.Parent;
        }
        try
        {
            var d = DeviceDisplay.Current.MainDisplayInfo;
            var h = d.Height / d.Density;
            if (h > 0) return h;
        }
        catch { }
        return 800;
    }
}