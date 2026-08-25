using System.Collections.ObjectModel;
using CatClawMusic.Plugins.Lrclib.Lyrico;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Storage;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 插件管理器 ViewModel：统一管理 Lyrico 源插件（对齐 Lyrico PluginManagerScreen）。
/// 列出全部已装源插件（名称/能力/加载状态/启停开关），支持导入 .zip、启停、配置、测试、卸载。
/// </summary>
public partial class PluginManagerViewModel : ObservableObject
{
    private readonly LyricoLyricsHub? _hub;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusText = "加载源插件中...";

    public ObservableCollection<PluginSourceItem> Sources { get; } = new();

    public bool HasHub => _hub is not null;

    public PluginManagerViewModel(LyricoLyricsHub? hub)
    {
        _hub = hub;
        RefreshSources();
    }

    /// <summary>刷新已安装源插件列表（加载/导入/卸载/启停后调用）。</summary>
    public void RefreshSources()
    {
        Sources.Clear();
        var hub = _hub;
        if (hub == null)
        {
            StatusText = "插件宿主未初始化（LyricoHub 不可用）";
            return;
        }

        try
        {
            var any = false;
            foreach (var (dir, name, status) in hub.GetSourceInfos())
            {
                var manifest = hub.GetManifest(dir);
                Sources.Add(new PluginSourceItem
                {
                    Dir = dir,
                    Name = name,
                    Status = status,
                    HasConfig = (manifest?.ConfigFields?.Count ?? 0) > 0,
                    CapabilityText = CapabilityTextOf(manifest),
                    IsEnabled = hub.IsSourceEnabled(dir),
                });
                any = true;
            }
            StatusText = any ? $"共 {Sources.Count} 个源插件" : "未安装任何 Lyrico 源插件（点右上角「导入」安装 .zip）";
        }
        catch (Exception ex)
        {
            StatusText = "刷新失败：" + ex.Message;
        }
    }

    private static string CapabilityTextOf(LyricoManifest? manifest)
    {
        if (manifest == null || (manifest.Capabilities?.Count ?? 0) == 0)
            return "未知能力";

        string CapName(string c) => c switch
        {
            "getLyrics" => "歌词",
            "searchSongs" => "歌曲",
            "searchCovers" => "封面",
            _ => c,
        };
        return string.Join(" · ", manifest.Capabilities.Select(CapName));
    }

    /// <summary>导入 Lyrico 源插件 .zip（FilePicker → 解包校验 → 装入并刷新）。</summary>
    [RelayCommand]
    private async Task ImportAsync()
    {
        var hub = _hub;
        if (hub == null) return;

        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.WinUI] = new[] { ".zip" },
                    [DevicePlatform.Android] = new[] { "application/zip", "application/octet-stream" },
                    [DevicePlatform.iOS] = new[] { "public.zip-archive" },
                    [DevicePlatform.MacCatalyst] = new[] { "public.zip-archive" },
                }),
            }).ConfigureAwait(false);
            if (result == null) return;

            IsBusy = true;
            StatusText = "导入中...";
            var r = await LyricoSourceInstaller.ImportAsync(result.FullPath, hub);
            StatusText = r.Success ? $"{r.Message}（现共 {Sources.Count + 1} 个源）" : r.Message;
            if (r.Success) RefreshSources();
        }
        catch (Exception ex)
        {
            StatusText = "导入失败：" + ex.Message;
        }
        finally { IsBusy = false; }
    }

    /// <summary>启用/禁用源（禁用的源在歌词兜底时跳过）。</summary>
    [RelayCommand]
    private void ToggleSource(PluginSourceItem? item)
    {
        var hub = _hub;
        if (hub == null || item == null) return;
        var enable = !item.IsEnabled;
        hub.SetSourceEnabled(item.Dir, enable);
        item.IsEnabled = enable;
        RefreshSources();
        StatusText = enable ? $"已启用「{item.Name}」" : $"已禁用「{item.Name}」";
    }

    /// <summary>卸载源插件（删除目录 + 刷新内存）。</summary>
    [RelayCommand]
    private void DeleteSource(PluginSourceItem? item)
    {
        var hub = _hub;
        if (hub == null || item == null) return;
        var removed = hub.DeleteSource(item.Dir);
        if (removed != null)
        {
            RefreshSources();
            StatusText = $"已卸载「{item.Name}」（现共 {Sources.Count} 个源）";
        }
        else
        {
            StatusText = $"卸载失败：{item.Name}";
        }
    }

    /// <summary>打开源配置页（仅声明了 configFields 的源可配置）。</summary>
    [RelayCommand]
    private async Task OpenConfigAsync(PluginSourceItem? item)
    {
        var hub = _hub;
        if (hub == null || item == null) return;
        await PluginNav.PushAsync(new LyricoSourceConfigPage(new LyricoSourceConfigViewModel(hub, item.Dir)));
    }

    /// <summary>打开源测试页（输入歌曲信息验证该源取词）。</summary>
    [RelayCommand]
    private async Task OpenTestAsync(PluginSourceItem? item)
    {
        var hub = _hub;
        if (hub == null || item == null) return;
        await PluginNav.PushAsync(new LyricoSourceTestPage(new LyricoSourceTestViewModel(hub, item.Dir)));
    }
}

/// <summary>插件管理列表条目。</summary>
public partial class PluginSourceItem : ObservableObject
{
    public string Dir { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CapabilityText { get; set; } = string.Empty;
    public bool HasConfig { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleText))]
    private bool isEnabled = true;

    public string NameWithStatus => string.IsNullOrEmpty(Status) || Status == "已加载"
        ? Name
        : $"{Name}（{Status}）";

    public string ToggleText => IsEnabled ? "禁用" : "启用";
}