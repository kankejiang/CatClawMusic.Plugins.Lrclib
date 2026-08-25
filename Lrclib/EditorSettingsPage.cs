using Microsoft.Maui.Controls;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 编辑设置入口页（Lyrico 设置中心的编辑相关分组）：
/// 提供「编辑字段可见性」与「自定义标签」两个入口。
/// </summary>
public class EditorSettingsPage : ContentPage
{
    public EditorSettingsPage()
    {
        Title = "编辑设置";
        BackgroundColor = ThemeHelper.Color("WindowBackgroundColor", "#1A1838");
        Content = new ScrollView { Content = BuildContent() };
    }

    private View BuildContent()
    {
        var stack = new VerticalStackLayout { Spacing = 10, Padding = new Thickness(12, 8) };
        stack.Add(MakeEntry("编辑字段可见性", "控制编辑标签页显示哪些字段", () => PluginNav.PushAsync(new EditFieldVisibilityPage())));
        stack.Add(MakeEntry("自定义标签", "管理要在编辑页显示的自定义标签键", () => PluginNav.PushAsync(new CustomTagManagementPage())));
        stack.Add(MakeEntry("艺术家拆分设置", "配置艺人库如何拆分多艺人", () => PluginNav.PushAsync(new ArtistSplitSettingsPage())));
        stack.Add(MakeEntry("歌词清理规则", "配置标签行过滤关键词与去空行默认", () => PluginNav.PushAsync(new LyricCleanupRulesPage())));
        stack.Add(MakeEntry("备份与恢复", "导出/导入插件全部用户配置", () => PluginNav.PushAsync(new SettingsBackupPage())));
        return stack;
    }

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