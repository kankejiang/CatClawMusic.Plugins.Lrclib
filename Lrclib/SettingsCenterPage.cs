using Microsoft.Maui.Controls;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 设置中心总页：把分散在插件各处的设置子页收敛成统一的入口（对齐 Lyrico SettingsScreen）。
/// 按 「歌词 / 编辑 / 数据 / 关于」分组，每项导航到具体子页。
/// 从音乐库主页顶栏「设置」进入。
/// </summary>
public class SettingsCenterPage : ContentPage
{
    public SettingsCenterPage()
    {
        Title = "设置";
        BackgroundColor = ThemeHelper.Color("WindowBackgroundColor", "#1A1838");
        Content = new ScrollView { Content = BuildContent() };
    }

    private View BuildContent()
    {
        var about = LyricoUi.Card(new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                L(13, FontAttributes.Bold, "猫爪音乐 · Lyrico 插件 v1.2.0"),
                L(12, FontAttributes.None,
                    "LRCLIB 开放歌词库 + Lyrico 多源歌词兜底\n音乐库浏览 / 标签编辑 / 批量操作 / 源插件管理"),
            },
        });

        var stack = new VerticalStackLayout
        {
            Spacing = 8,
            Padding = new Thickness(12, 8),
            Children =
            {
                GroupTitle("歌词"),
                MakeEntry("歌词匹配", "手动搜索 LRCLIB 候选并指定使用歌词", OpenManualMatch),
                MakeEntry("源插件管理", "Lyrico 源插件：导入/启停/配置/测试/卸载", OpenPluginManager),

                GroupTitle("编辑"),
                MakeEntry("编辑字段可见性", "控制编辑标签页显示哪些字段", () => PluginNav.PushAsync(new EditFieldVisibilityPage())),
                MakeEntry("自定义标签", "管理要在编辑页显示的自定义标签键", () => PluginNav.PushAsync(new CustomTagManagementPage())),
                MakeEntry("艺术家拆分设置", "配置艺人库如何拆分多艺人", () => PluginNav.PushAsync(new ArtistSplitSettingsPage())),
                MakeEntry("歌词清理规则", "配置标签行过滤关键词与去空行默认", () => PluginNav.PushAsync(new LyricCleanupRulesPage())),
                MakeEntry("字符映射", "配置文件名非法字符的替换规则", () => PluginNav.PushAsync(new CharacterMappingPage())),

                GroupTitle("数据"),
                MakeEntry("备份与恢复", "导出/导入插件全部用户配置（.zip）", () => PluginNav.PushAsync(new SettingsBackupPage())),
                MakeEntry("批量任务历史", "查看已执行的后台批量任务记录与逐首明细", () => PluginNav.PushAsync(new BatchTaskListPage())),

                GroupTitle("关于"),
                about,
            },
        };
        return stack;
    }

    private static Label GroupTitle(string text)
    {
        var label = ThemeHelper.Label(12, FontAttributes.Bold, "TextSecondaryColor", "#C2C6E4", true);
        label.Text = text;
        label.Margin = new Thickness(6, 10, 6, 2);
        return label;
    }

    private static async Task OpenManualMatch()
    {
        if (PluginHost.LrclibClient is { } c && PluginHost.OverrideStore is { } os
            && PluginHost.LyricoHub is { } hub && PluginHost.Services is { } svcs)
        {
            await PluginNav.PushAsync(new ManualMatchPage(new ManualMatchViewModel(c, os, hub, svcs)));
        }
    }

    private static Task OpenPluginManager() => PluginNav.PushAsync(new PluginManagerPage());

    /// <summary>主题色文本标签（主色 / 次要色）。</summary>
    private static Label L(double size, FontAttributes weight, string text)
    {
        var l = ThemeHelper.Label(size, weight, "TextPrimaryColor", "#F7F8FF", true);
        l.Text = text;
        return l;
    }

    /// <summary>设置项入口行：标题 + 副标题 + 箭头，点击导航。</summary>
    private static View MakeEntry(string title, string subtitle, Func<Task> navigate)
    {
        var titleLabel = ThemeHelper.Label(15, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
        titleLabel.Text = title;
        var subtitleLabel = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        subtitleLabel.Text = subtitle;

        var text = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Spacing = 2,
            Children = { titleLabel, subtitleLabel },
        };

        var arrow = ThemeHelper.Label(16, FontAttributes.Bold, "TextSecondaryColor", "#C2C6E4", false);
        arrow.Text = "›";
        arrow.VerticalOptions = LayoutOptions.Center;

        var row = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        row.Add(text, 0);
        row.Add(arrow, 1);

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await navigate();
        row.GestureRecognizers.Add(tap);

        return LyricoUi.Card(row);
    }
}