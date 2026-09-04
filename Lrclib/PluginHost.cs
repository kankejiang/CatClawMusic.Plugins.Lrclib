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
    /// <summary>宿主 IServiceProvider（CreateEntryPage 时注入；直入路径按需兜底，见 <see cref="Get{T}"/>）</summary>
    public static IServiceProvider? Services { get; set; }

    /// <summary>解析宿主服务，解析不到返回 null。
    /// Services 未注入（如宿主播放页「更多」菜单直入编辑/搜索页）时，
    /// 从当前 MAUI 应用 DI 容器兜底解析宿主服务（IAudioFileService 等）。</summary>
    public static T? Get<T>() where T : class
    {
        if (Services is { } sp)
        {
            try { return sp.GetService(typeof(T)) as T; } catch { }
        }
        Services ??= ResolveAppServices();
        try { return Services?.GetService(typeof(T)) as T; } catch { return null; }
    }

    /// <summary>从当前 MAUI 应用取宿主 DI 容器（跨平台，任何页面/后台上下文可用）。</summary>
    private static IServiceProvider? ResolveAppServices()
    {
        try { return Microsoft.Maui.IPlatformApplication.Current?.Services; }
        catch { return null; }
    }

    /// <summary>
    /// 补齐插件注入：宿主播放页「更多」菜单等直入路径（未经过 <see cref="LrclibLyricsPlugin.CreateEntryPage"/>）
    /// 时调用，注入插件内部单例，保证编辑标签页能读封面/歌词、统一搜索页带上 Lyrico 多源。
    /// </summary>
    public static void EnsureInjected(LrclibApiClient? client, OverrideStore? store, Lyrico.LyricoLyricsHub? hub)
    {
        LrclibClient ??= client;
        OverrideStore ??= store;
        LyricoHub ??= hub;
        Services ??= ResolveAppServices();
    }

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
/// 标签写入通知：记录「文件已被写入」的路径，供编辑页等返回时检测并重载。
/// 采用静态消费式标记（Take 后清除），避免事件订阅的生命周期问题。
/// </summary>
internal static class TagWriteNotifier
{
    private static readonly HashSet<string> _written = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>记录文件已被写入（如统一搜索页「写入」成功后调用）</summary>
    public static void Raise(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        lock (_written) _written.Add(filePath);
    }

    /// <summary>检查并消费标记：该文件自上次检查后是否被写入过</summary>
    public static bool Take(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        lock (_written)
        {
            if (!_written.Contains(filePath)) return false;
            _written.Remove(filePath);
            return true;
        }
    }
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
        // 所有承载方式统一注入叠放式返回头（浮在顶部），彻底规避插件容器里
        // Grid 行布局不可靠导致的返回键跑位（Android Shell 与 Windows 桌面一致）。
        if (page is ContentPage cp)
        {
            AddBackHeader(cp);
            cp.AttachSafeArea();
        }

        if (TryGetShellNavigation() is { } shellNav)
        {
            await shellNav.PushAsync(page);
            return;
        }

        // 桌面：窗口导航只支持模态。用 NavigationPage 提供子页栈。
        var main = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (main == null) return;

        if (_modalRoot == null)
        {
            // 根页自己也带叠放返回头（shell 无返回箭头时用），并禁掉 NavigationPage
            // 系统返回条，避免双返回、且不受系统导航栏位置影响。
            if (page is ContentPage rootCp)
            {
                NavigationPage.SetHasNavigationBar(rootCp, false);
                _modalRoot = new NavigationPage(rootCp);
            }
            else
            {
                _modalRoot = new NavigationPage(page);
            }
            await main.Navigation.PushModalAsync(_modalRoot);
        }
        else
        {
            if (page is ContentPage child) NavigationPage.SetHasNavigationBar(child, false);
            await _modalRoot.Navigation.PushAsync(page);
        }
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
