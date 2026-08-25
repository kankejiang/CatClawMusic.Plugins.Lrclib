using System.IO.Compression;
using System.Text.Json;
using CatClawMusic.Plugins.Lrclib.Lyrico;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 插件设置备份/恢复服务：把所有用户配置打包成一个 .zip（桌面端文件 I/O）。
/// <para>覆盖：lrclib_overrides.json / editor_settings.json / artist_split_config.json /
/// lrclib_cleanup_rules.json（数据目录）+ LyricoSources/.config/ 下全部 *.json（源配置/禁用列表）。</para>
/// <para>不含：Lyrico 源插件脚本本身（体积大，由用户单独导入）、宿主数据。</para>
/// </summary>
public static class SettingsBackupService
{
    /// <summary>插件数据目录（lrclib_*.json 所在）。</summary>
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CatClawMusic.Maui");

    /// <summary>Lyrico 源配置目录。</summary>
    private static string LyricoConfigDir => Path.Combine(
        LyricoSourceCatalog.SourcesRoot, ".config");

    /// <summary>备份的配置文件名清单（数据目录内）。</summary>
    private static readonly string[] DataFiles =
        { "lrclib_overrides.json", "editor_settings.json", "artist_split_config.json", "lrclib_cleanup_rules.json" };

    /// <summary>备份：把所有配置打包到指定 .zip 路径。返回写入的文件数，失败返回 -1。</summary>
    public static int Backup(string zipPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(zipPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            int count = 0;
            using (var fs = File.Create(zipPath))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var name in DataFiles)
                {
                    var src = Path.Combine(DataDir, name);
                    if (File.Exists(src))
                    {
                        zip.CreateEntryFromFile(src, "data/" + name);
                        count++;
                    }
                }
                // Lyrico 源配置目录下全部 *.json
                if (Directory.Exists(LyricoConfigDir))
                {
                    foreach (var f in Directory.EnumerateFiles(LyricoConfigDir, "*.json"))
                    {
                        zip.CreateEntryFromFile(f, "lyrico/" + Path.GetFileName(f));
                        count++;
                    }
                }
            }
            return count;
        }
        catch { return -1; }
    }

    /// <summary>恢复：从 .zip 还原所有配置（覆盖现有）。返回还原的文件数，失败返回 -1。</summary>
    public static (int Count, string Detail) Restore(string zipPath)
    {
        try
        {
            if (!File.Exists(zipPath)) return (-1, "备份文件不存在");
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(LyricoConfigDir);
            int count = 0;
            using var fs = File.OpenRead(zipPath);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                string dest;
                if (entry.FullName.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
                    dest = Path.Combine(DataDir, entry.Name);
                else if (entry.FullName.StartsWith("lyrico/", StringComparison.OrdinalIgnoreCase))
                    dest = Path.Combine(LyricoConfigDir, entry.Name);
                else continue;
                entry.ExtractToFile(dest, overwrite: true);
                count++;
            }
            return (count, $"已还原 {count} 个配置文件");
        }
        catch (Exception ex) { return (-1, "恢复失败：" + ex.Message); }
    }
}
