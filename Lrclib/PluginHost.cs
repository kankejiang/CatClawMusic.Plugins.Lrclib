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
}

/// <summary>
/// 插件内部导航助手：页面统一经宿主 Shell 导航栈 Push/Pop。
/// 桌面无 Shell 窗口时回退到主页面导航栈，保证两端可用。
/// </summary>
internal static class PluginNav
{
    public static INavigation? CurrentNavigation
        => Shell.Current?.Navigation
           ?? Application.Current?.MainPage?.Navigation
           ?? (Shell.Current?.CurrentPage as ContentPage)?.Navigation;

    public static Task PushAsync(Page page)
        => CurrentNavigation?.PushAsync(page) ?? Task.CompletedTask;

    public static Task PopAsync()
        => CurrentNavigation is { } nav && nav.NavigationStack.Count > 0
            ? nav.PopAsync()
            : Task.CompletedTask;
}
