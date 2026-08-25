using Microsoft.Maui.Controls;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 设置备份与恢复页（Lyrico <c>SettingBackup</c> 复刻）：
/// 把插件全部用户配置（覆盖记录/清理规则/艺人拆分/字段可见性/源配置/禁用列表）
/// 打包成 .zip 导出，或从 .zip 还原。桌面端用 File I/O。
/// </summary>
public class SettingsBackupPage : ContentPage
{
    private readonly Entry _backupPath = new();
    private readonly Entry _restorePath = new();
    private readonly Label _status = new();

    public SettingsBackupPage()
    {
        Title = "备份与恢复";
        BackgroundColor = ThemeHelper.Color("WindowBackgroundColor", "#1A1838");

        var hint = new Label
        {
            Text = "备份包含：歌词覆盖记录、清理规则、艺人拆分、字段可见性、源插件配置与启停。不含源脚本本身（需单独导入）。",
            FontSize = 11,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        hint.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");

        // ── 备份 ──
        var backupHeader = MakeLabel("导出备份");
        _backupPath.Placeholder = "导出路径（如 D:\\lrclib_backup.zip）";
        BindEntry(_backupPath);
        var backupButton = MakeButton("导出备份", Colors.White, true);
        backupButton.Clicked += (_, _) =>
        {
            var p = _backupPath.Text?.Trim();
            if (string.IsNullOrEmpty(p)) { _status.Text = "请填写导出路径"; return; }
            var n = SettingsBackupService.Backup(p);
            _status.Text = n >= 0 ? $"已导出 {n} 个配置文件到 {p}" : "导出失败（路径不可写？）";
        };

        // ── 恢复 ──
        var restoreHeader = MakeLabel("导入备份");
        _restorePath.Placeholder = "备份文件路径（.zip）";
        BindEntry(_restorePath);
        var restoreButton = MakeButton("导入并覆盖", Colors.White, true);
        restoreButton.Clicked += async (_, _) =>
        {
            var p = _restorePath.Text?.Trim();
            if (string.IsNullOrEmpty(p)) { _status.Text = "请填写备份文件路径"; return; }
            var confirm = await Shell.Current?.DisplayAlert("确认恢复",
                "恢复会覆盖当前所有插件配置，确定继续吗？", "恢复", "取消");
            if (confirm != true) return;
            var (count, detail) = SettingsBackupService.Restore(p);
            _status.Text = count >= 0 ? $"恢复完成：{detail}" : detail;
        };

        _status.FontSize = 12;
        _status.LineBreakMode = LineBreakMode.WordWrap;
        _status.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(16, 12),
                Spacing = 8,
                Children = { hint, backupHeader, _backupPath, backupButton, restoreHeader, _restorePath, restoreButton, _status },
            }
        };
    }

    private static Label MakeLabel(string text)
    {
        var l = new Label { Text = text, FontSize = 13, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 8, 0, 0) };
        l.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
        return l;
    }

    private static Button MakeButton(string text, Color textColor, bool filled)
    {
        var b = new Button { Text = text, FontSize = 14, TextColor = textColor, CornerRadius = 14, Padding = new Thickness(16, 8) };
        if (filled) b.BackgroundColor = ThemeHelper.Color("PrimaryColor", "#8C7BFF");
        else { b.SetDynamicResource(Button.TextColorProperty, "PrimaryColor"); b.BackgroundColor = Colors.Transparent; }
        return b;
    }

    private static void BindEntry(Entry e)
    {
        e.FontSize = 14;
        e.SetDynamicResource(Entry.TextColorProperty, "TextPrimaryColor");
        e.SetDynamicResource(Entry.PlaceholderColorProperty, "TextSecondaryColor");
    }
}
