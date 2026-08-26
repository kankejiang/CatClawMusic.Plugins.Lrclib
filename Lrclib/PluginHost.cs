using System.Linq;
using Microsoft.Maui.Controls;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 插件宿主服务定位器：宿主在创建入口页时把 <see cref="IServiceProvider"/> 注入进来，
/// 插件各页面统一从这里解析宿主服务（音乐库、音频文件读写、播放等），避免层层传参。
/// </summary>
internal static class PluginHost
{
    /// <summary>宿主 IServiceProvider（CreateEntryPage 时注入，之后全插件共享）</summary>
    public static IServiceProvider? Services { get; set; }

    /// <summary>解析宿主服务，解析不到返回 null</summary>
    public static T? Get<T>() where T : class
        => Services?.GetService(typeof(T)) as T;

    /// <summary>外部 Lyrico 源插件的目录名（位于宿主 AppDataDirectory 下）。</summary>
    public const string LyricoSourcesDirName = "Plugin/LyricoSources";

    /// <summary>音乐库服务（宿主已扫描的本地音乐）</summary>
    public static IMusicLibraryService? Library => Get<IMusicLibraryService>();

    /// <summary>音频文件读写服务（标签/封面/歌词/重命名/删除）</summary>
    public static IAudioFileService? AudioFiles => Get<IAudioFileService>();

    // ── 插件内部单例（CreateEntryPage 时注入，供各页面访问，避免层层传参）──

    /// <summary>LRCLIB HTTP 客户端（歌词匹配/批量匹配复用同一实例）</summary>
    public static LrclibApiClient? LrclibClient { get; set; }

    /// <summary>手动覆盖记录存储（歌词匹配页用）</summary>
    public static OverrideStore? OverrideStore { get; set; }

    /// <summary>Lyrico 多源歌词编排 hub（LRCLIB 未命中时的兜底）</summary>
    public static Lyrico.LyricoLyricsHub? LyricoHub { get; set; }
}

/// <summary>
/// 插件内部导航助手。
/// Android 宿主有 Shell，走 Shell 导航栈；Windows 桌面宿主无 Shell，
/// 且窗口导航（Window.NavigationImpl）不支持非模态 PushAsync，
/// 因此桌面端回退到窗口级模态导航：首次 Push 用 NavigationPage 包装
/// 并以模态推入，后续 Push 进入其内层栈，子页自动获得系统返回条。
/// </summary>
internal static class PluginNav
{
    // 桌面端模态导航根。首次 Push 时创建并以模态推入；之后 Push 进内层栈。
    private static NavigationPage? _modalRoot;

    /// <summary>安全获取 Shell 导航；无 Shell 时返回 null（宿主桌面端即如此）。</summary>
    static INavigation? TryGetShellNavigation()
    {
        try
        {
            if (Shell.Current is { } shell)
                return shell.Navigation;
        }
        catch { }
        return null;
    }

    public static async Task PushAsync(Page page)
    {
        if (page is ContentPage cp)
        {
            if (TryGetShellNavigation() != null)
            {
                // Android Shell 路径：宿主隐藏导航栏 → 注入返回头 + 安全区
                AddBackHeader(cp);
                cp.AttachSafeArea();
            }
            else
            {
                // 桌面/无 Shell：WrapRoot 提供返回头，这里只做安全区
                cp.AttachSafeArea();
            }
        }

        if (TryGetShellNavigation() is { } shellNav)
        {
            await shellNav.PushAsync(page);
            return;
        }

        // 桌面：窗口导航只支持模态。用 NavigationPage 包一层获得子页栈与返回条。
        var main = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (main == null) return;

        if (_modalRoot == null)
        {
            // 首个被压入的页面是 NavigationPage 根，系统不会为根绘制返回箭头；
            // 故用带统一返回按钮的包装页承载其内容，保证能返回主界面。
            var rootPage = page is ContentPage rootContent ? WrapRoot(rootContent) : page;
            if (rootPage is ContentPage rootCp) rootCp.AttachSafeArea();   // 包装页返回头同样避让状态栏
            _modalRoot = new NavigationPage(rootPage);
            await main.Navigation.PushModalAsync(_modalRoot);
        }
        else
        {
            await _modalRoot.Navigation.PushAsync(page);
        }
    }

    /// <summary>把页面内容装进带统一返回按钮的根包装页（桌面回主界面入口）。</summary>
    static ContentPage WrapRoot(ContentPage page)
    {
        var body = page.Content;
        if (body != null)
        {
            body.BindingContext = page.BindingContext;
            page.Content = null;
        }

        var back = new Button
        {
            Text = "‹",
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = Colors.Transparent,
            TextColor = ThemeHelper.Color("TextPrimaryColor", "#F7F8FF"),
            WidthRequest = 44,
            HeightRequest = 40,
            Padding = new Thickness(8, 0),
            VerticalOptions = LayoutOptions.Center,
        };
        back.Clicked += async (_, _) => await PopAsync();

        var title = new Label
        {
            Text = page.Title ?? string.Empty,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeHelper.Color("TextPrimaryColor", "#F7F8FF"),
            VerticalOptions = LayoutOptions.Center,
        };

        var header = new HorizontalStackLayout
        {
            Spacing = 4,
            Padding = new Thickness(8, 8, 12, 4),
            Children = { back, title },
        };

        var root = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        root.Add(header, 0);
        if (body != null) root.Add(body, 1);

        var wrapper = new ContentPage { Title = page.Title ?? string.Empty, Content = root };
        return wrapper;
    }

    public static async Task PopAsync()
    {
        if (_modalRoot is { } root)
        {
            if (root.Navigation.NavigationStack.Count > 1)
                await root.Navigation.PopAsync();
            else if (Application.Current?.Windows.FirstOrDefault()?.Page is { } main)
            {
                await main.Navigation.PopModalAsync();
                _modalRoot = null;
            }
            return;
        }

        if (TryGetShellNavigation() is { } shellNav && shellNav.NavigationStack.Count > 0)
            await shellNav.PopAsync();
    }

    /// <summary>
    /// 给页面注入返回按钮头（Android Shell 路径专用——宿主隐藏导航栏时页面无系统返回箭头）。
    /// 把原 Content 包进外层 Grid：第 0 行 = ‹ 返回键 + 页面标题，第 1 行 = 原内容。
    /// 随后的 AttachSafeArea 会作用于外层根，返回头自动避让状态栏。
    /// </summary>
    private static void AddBackHeader(ContentPage page)
    {
        // 兼容非 Layout Content（如 ScrollView 等）：统一装入外层 Grid，
        // 确保返回头对任何详情页都能注入。
        var body = page.Content;
        if (body == null) return;
        if (body is Grid g && g.ClassId == "plugin-nav-wrap") return;   // 幂等保护

        var back = new Border
        {
            WidthRequest = 40,
            HeightRequest = 32,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(10) },
            BackgroundColor = ThemeHelper.Color("CardBackgroundColor", "#1AFFFFFF"),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(10, 2, 0, 2),
            Content = new Label
            {
                Text = "‹",
                FontSize = 20,
                FontAttributes = FontAttributes.Bold,
                TextColor = ThemeHelper.Color("TextPrimaryColor", "#F7F8FF"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalOptions = LayoutOptions.Center,
            },
        };
        back.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                try { await PopAsync(); } catch { }
            }),
        });

        var title = ThemeHelper.Label(15, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
        title.Text = page.Title ?? string.Empty;
        title.VerticalOptions = LayoutOptions.Center;
        title.Margin = new Thickness(8, 0, 0, 0);

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            Padding = new Thickness(0, 2, 12, 2),
        };
        header.Add(back, 0, 0);
        header.Add(title, 1, 0);

        // 用叠放布局：返回头浮在顶部，body 顶部留 40dp 避让。
        // 比 Grid 行布局更稳，跨平台（尤其 Windows 模态页）都能保证返回头在左上角。
        var outer = new Grid
        {
            ClassId = "plugin-nav-wrap",
        };

        // body 包一层 ContentView，顶部让出返回头高度（不修改 body 自身 padding）
        var bodyWrapper = new ContentView
        {
            Content = body,
            Padding = new Thickness(0, 40, 0, 0),
        };
        outer.Add(bodyWrapper);

        header.VerticalOptions = LayoutOptions.Start;
        header.HorizontalOptions = LayoutOptions.Fill;
        header.HeightRequest = 40;
        outer.Add(header);
        page.Content = outer;
    }
}
