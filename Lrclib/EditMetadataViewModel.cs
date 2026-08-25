using CatClawMusic.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 元数据编辑 ViewModel（Lyrico EditMetadata 复刻）：读取歌曲当前标签，
/// 编辑 标题/艺人/专辑/专辑艺人/年份/流派/音轨/歌词/封面，经宿主 <see cref="IAudioFileService"/> 写回文件。
/// </summary>
public partial class EditMetadataViewModel : ObservableObject
{
    private readonly IAudioFileService? _audio;
    private readonly string _uri;
    private readonly EditorSettingsStore _settingsStore = new();

    /// <summary>编辑设置（字段可见性等），供页面按场景渲染字段</summary>
    public EditorSettings Settings => _settingsStore.Get();

    public SongItem Song { get; }

    [ObservableProperty] private bool isLoading = true;
    [ObservableProperty] private bool isSaving;
    [ObservableProperty] private string statusText = "正在读取标签...";
    [ObservableProperty] private string title = "";
    [ObservableProperty] private string artist = "";
    [ObservableProperty] private string album = "";
    [ObservableProperty] private string albumArtist = "";
    [ObservableProperty] private string year = "";
    [ObservableProperty] private string genre = "";
    [ObservableProperty] private string trackNumber = "";
    [ObservableProperty] private string discNumber = "";
    [ObservableProperty] private string composer = "";
    [ObservableProperty] private string lyricist = "";
    [ObservableProperty] private string comment = "";
    [ObservableProperty] private string copyright = "";
    [ObservableProperty] private string lyrics = "";
    [ObservableProperty] private ImageSource? coverSource;
    [ObservableProperty] private bool hasCover;

    /// <summary>封面写入意图：null=保持不变，空数组=清除，非空=替换</summary>
    private byte[]? _coverIntent;

    /// <summary>已加载的自定义标签（可见键），用于判断是否有改动</summary>
    private Dictionary<string, string> _loadedCustom = new();

    /// <summary>被改动过的自定义标签：可见键 → 当前值（空字符串表示移除）</summary>
    private readonly Dictionary<string, string> _customTouched = new();

    public EditMetadataViewModel(SongItem song, IAudioFileService? audio)
    {
        Song = song;
        _audio = audio;
        _uri = song.FilePath;
        Title = song.Title;
        Artist = song.Artist;
    }

    public async Task LoadAsync()
    {
        if (string.IsNullOrWhiteSpace(_uri))
        {
            StatusText = "无本地文件路径";
            IsLoading = false;
            return;
        }

        try
        {
            var tag = _audio is null ? null : await _audio.ReadTagsAsync(_uri);
            if (tag == null)
            {
                StatusText = "未能读取标签（文件不存在或不受支持）";
            }
            else
            {
                Title = tag.Title ?? Title;
                Artist = tag.Artist ?? Artist;
                Album = tag.Album ?? "";
                AlbumArtist = tag.AlbumArtist ?? "";
                Year = tag.Year ?? "";
                Genre = tag.Genre ?? "";
                TrackNumber = tag.TrackNumber ?? "";
                DiscNumber = tag.DiscNumber ?? "";
                Composer = tag.Composer ?? "";
                Lyricist = tag.Lyricist ?? "";
                Comment = tag.Comment ?? "";
                Copyright = tag.Copyright ?? "";
                _loadedCustom = new Dictionary<string, string>(tag.CustomTags ?? new());
                Lyrics = tag.Lyrics ?? "";
                if (tag.Cover is { Length: > 0 })
                {
                    CoverSource = ImageSource.FromStream(() => new MemoryStream(tag.Cover));
                    HasCover = true;
                }
                StatusText = "标签已加载，修改后点「保存」写回文件";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"读取失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>选择新封面（系统图片选择器）</summary>
    public async Task PickCoverAsync()
    {
        try
        {
            var result = await Microsoft.Maui.Media.MediaPicker.Default.PickPhotoAsync(
                new Microsoft.Maui.Media.MediaPickerOptions { Title = "选择封面" });
            if (result == null) return;

            await using var stream = await result.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var bytes = ms.ToArray();
            if (bytes.Length == 0) return;

            _coverIntent = bytes;
            CoverSource = ImageSource.FromStream(() => new MemoryStream(bytes));
            HasCover = true;
            StatusText = "已选择新封面，保存时写入";
        }
        catch (Exception ex)
        {
            StatusText = $"选择封面失败：{ex.Message}";
        }
    }

    /// <summary>清除封面（保存时移除内嵌封面）</summary>
    public void ClearCover()
    {
        _coverIntent = Array.Empty<byte>();
        CoverSource = null;
        HasCover = false;
        StatusText = "将清除封面，保存时写回";
    }

    /// <summary>当前可在编辑页显示的自定义标签可见键（规范化大写，来自设置）</summary>
    public IReadOnlyList<string> VisibleCustomKeys
        => Settings.CustomVisibleKeys
            .Select(EditorSettingsStore.NormalizeKey)
            .Where(k => k != null)
            .Cast<string>()
            .ToList();

    /// <summary>自定义标签键当前值（编辑中或已加载均返回；无则空串）</summary>
    public string GetCustomValue(string key)
    {
        if (_customTouched.TryGetValue(key, out var v)) return v;
        return _loadedCustom.TryGetValue(key, out var loaded) ? loaded : "";
    }

    /// <summary>记录自定义标签键改动（空串=移除）</summary>
    public void SetCustomValue(string key, string value)
        => _customTouched[key] = value;

    public async Task<bool> SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_uri) || _audio is null)
        {
            StatusText = "无法保存：无文件或宿主未提供写文件服务";
            return false;
        }

        IsSaving = true;
        try
        {
            var edit = new CatClawMusic.Core.Models.AudioTagEdit
            {
                Title = Title,
                Artist = Artist,
                Album = Album,
                AlbumArtist = AlbumArtist,
                Year = Year,
                Genre = Genre,
                TrackNumber = TrackNumber,
                DiscNumber = DiscNumber,
                Composer = Composer,
                Lyricist = Lyricist,
                Comment = Comment,
                Copyright = Copyright,
                CustomTags = _customTouched.Count > 0 ? new Dictionary<string, string>(_customTouched) : null,
                Lyrics = Lyrics,
                Cover = _coverIntent, // null=保留
            };
            var ok = await _audio.WriteTagsAsync(_uri, edit);
            StatusText = ok ? "已保存到文件" : "保存失败（文件不可写？）";
            return ok;
        }
        catch (Exception ex)
        {
            StatusText = $"保存失败：{ex.Message}";
            return false;
        }
        finally
        {
            IsSaving = false;
        }
    }
}
