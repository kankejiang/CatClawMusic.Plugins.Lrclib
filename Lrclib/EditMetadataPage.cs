using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>元数据编辑页（Lyrico EditMetadata 复刻）：编辑标签字段并写回音频文件</summary>
public class EditMetadataPage : ContentPage
{
    private readonly EditMetadataViewModel _vm;

    public EditMetadataPage(SongItem song)
    {
        _vm = new EditMetadataViewModel(song, PluginHost.AudioFiles);
        BindingContext = _vm;

        Title = "编辑标签";
        BackgroundColor = ThemeHelper.Color("WindowBackgroundColor", "#1A1838");

        var topBar = BuildTopBar();
        var scroll = new ScrollView { Content = BuildContent() };

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
        };
        root.Add(topBar, 0, 0);
        root.Add(scroll, 0, 1);
        Content = root;
        WideAdapt.Attach(this, WideAdapt.FormMaxWidth);

        _ = _vm.LoadAsync();
    }

    /// <summary>
    /// 从搜索页返回时检测文件是否被「写入」过（歌词/封面/元数据直写文件），
    /// 是则重载标签——否则编辑框仍是旧值，再点「保存」会把旧歌词写回覆盖刚写入的内容。
    /// </summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (TagWriteNotifier.Take(_vm.Song.FilePath))
            _ = _vm.LoadAsync();
    }

    /// <summary>顶部操作栏：左「搜索」+ 中间标题 + 右「确认」（保存）。</summary>
    private View BuildTopBar()
    {
        var searchBtn = new Button
        {
            Text = "搜索",
            FontSize = 14,
            TextColor = ThemeHelper.Color("TextPrimaryColor", "#F7F8FF"),
            BackgroundColor = ThemeHelper.Color("CardBackgroundColor", "#1AFFFFFF"),
            CornerRadius = 16,
            HeightRequest = 36,
            Padding = new Thickness(14, 0),
            HorizontalOptions = LayoutOptions.Start,
        };
        searchBtn.Clicked += OnSearchClicked;

        var title = ThemeHelper.Label(17, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
        title.Text = "编辑标签";
        title.HorizontalOptions = LayoutOptions.Center;
        title.VerticalOptions = LayoutOptions.Center;

        var confirmBtn = new Button
        {
            Text = "确认",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = ThemeHelper.Color("PrimaryColor", "#8C7BFF"),
            CornerRadius = 16,
            HeightRequest = 36,
            Padding = new Thickness(14, 0),
            HorizontalOptions = LayoutOptions.End,
        };
        confirmBtn.Clicked += OnConfirmClicked;

        var bar = new Grid
        {
            Padding = new Thickness(16, 6, 16, 6),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        bar.Add(searchBtn, 0, 0);
        bar.Add(title, 1, 0);
        bar.Add(confirmBtn, 2, 0);
        return bar;
    }

    private async void OnSearchClicked(object? sender, EventArgs e)
    {
        await PluginNav.PushAsync(new UnifiedSearchPage(_vm.Song));
    }

    private async void OnConfirmClicked(object? sender, EventArgs e)
    {
        var btn = sender as Button;
        if (btn != null) btn.IsEnabled = false;
        var ok = await _vm.SaveAsync();
        if (btn != null) btn.IsEnabled = true;
        if (ok)
        {
            await DisplayAlert("已保存", "标签已写入音频文件。", "好");
        }
    }

    private View BuildContent()
    {
        const EditFieldScene scene = EditFieldScene.SingleEdit;
        var settings = _vm.Settings;

        var root = new VerticalStackLayout { Spacing = 12, Padding = new Thickness(16, 12) };
        if (IsFieldVisible("cover.picture", settings, scene)) root.Add(BuildCoverCard());
        if (IsFieldVisible("basic_info.title", settings, scene)
            || IsFieldVisible("basic_info.artist", settings, scene)
            || IsFieldVisible("basic_info.album", settings, scene)
            || IsFieldVisible("basic_info.album_artist", settings, scene)
            || IsFieldVisible("basic_info.date", settings, scene)
            || IsFieldVisible("basic_info.genre", settings, scene)
            || IsFieldVisible("track_details.track_number", settings, scene)
            || IsFieldVisible("track_details.disc_number", settings, scene)
            || IsFieldVisible("credits_other.composer", settings, scene)
            || IsFieldVisible("credits_other.lyricist", settings, scene)
            || IsFieldVisible("credits_other.comment", settings, scene)
            || IsFieldVisible("credits_other.copyright", settings, scene))
            root.Add(BuildFieldsCard(settings, scene));
        if (IsFieldVisible("lyrics.lyrics", settings, scene)) root.Add(BuildLyricsCard());
        var customKeys = _vm.VisibleCustomKeys;
        if (customKeys.Count > 0) root.Add(BuildCustomTagsCard(customKeys));
        root.Add(BuildSaveRow());
        return root;
    }

    private static bool IsFieldVisible(string code, EditorSettings settings, EditFieldScene scene)
        => EditFieldConfig.IsVisibleInScene(settings, EditFieldRegistry.FieldOf(code)!, scene);

    private View BuildCoverCard()
    {
        var preview = new Image
        {
            HeightRequest = 120,
            WidthRequest = 120,
            Aspect = Aspect.AspectFill,
            HorizontalOptions = LayoutOptions.Center,
        };
        preview.SetBinding(Image.SourceProperty, nameof(EditMetadataViewModel.CoverSource));
        preview.SetBinding(VisualElement.IsVisibleProperty, nameof(EditMetadataViewModel.HasCover));

        var placeholderLabel = new Label
        {
            Text = "♪",
            FontSize = 40,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        placeholderLabel.SetDynamicResource(Label.TextColorProperty, "PrimaryColor");
        var placeholder = new Border
        {
            HeightRequest = 120,
            WidthRequest = 120,
            StrokeThickness = 0,
            Background = ThemeHelper.Brush("CardBackgroundStrongColor", "#2DFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Content = placeholderLabel,
        };
        placeholder.SetBinding(VisualElement.IsVisibleProperty, new Binding(nameof(EditMetadataViewModel.HasCover))
        {
            Converter = new InvertBoolConverter(),
        });

        var coverBox = new Grid
        {
            HeightRequest = 120,
            WidthRequest = 120,
            HorizontalOptions = LayoutOptions.Center,
            Children = { placeholder, preview },
        };

        var pick = LyricoUi.ActionButton("选择封面", () => _ = _vm.PickCoverAsync());
        var clear = new Button
        {
            Text = "清除封面",
            FontSize = 13,
            TextColor = ThemeHelper.Color("TextSecondaryColor", "#C2C6E4"),
            BackgroundColor = Colors.Transparent,
            CornerRadius = 16,
            HeightRequest = 44,
        };
        clear.Clicked += (_, _) => _vm.ClearCover();

        var buttons = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
        };
        // Grid.Add(view, column, row)：显式写全，避免与 .Column() 互相覆盖
        buttons.Add(pick, 0, 0);
        buttons.Add(clear, 1, 0);

        var card = new VerticalStackLayout { Spacing = 10 };
        card.Add(coverBox);
        card.Add(buttons);
        return LyricoUi.Card(card);
    }

    private View BuildFieldsCard(EditorSettings settings, EditFieldScene scene)
    {
        var grid = new Grid
        {
            RowSpacing = 12,
        };

        // 编辑页可渲染的基础字段（code → 标签/绑定）；按可见性配置过滤
        var fields = new (string code, string label, string binding)[]
        {
            ("basic_info.title", "标题", nameof(EditMetadataViewModel.Title)),
            ("basic_info.artist", "艺人", nameof(EditMetadataViewModel.Artist)),
            ("basic_info.album", "专辑", nameof(EditMetadataViewModel.Album)),
            ("basic_info.album_artist", "专辑艺人", nameof(EditMetadataViewModel.AlbumArtist)),
            ("basic_info.date", "年份", nameof(EditMetadataViewModel.Year)),
            ("basic_info.genre", "流派", nameof(EditMetadataViewModel.Genre)),
            ("track_details.track_number", "音轨号", nameof(EditMetadataViewModel.TrackNumber)),
            ("track_details.disc_number", "碟号", nameof(EditMetadataViewModel.DiscNumber)),
            ("credits_other.composer", "作曲", nameof(EditMetadataViewModel.Composer)),
            ("credits_other.lyricist", "作词", nameof(EditMetadataViewModel.Lyricist)),
            ("credits_other.comment", "注释", nameof(EditMetadataViewModel.Comment)),
            ("credits_other.copyright", "版权", nameof(EditMetadataViewModel.Copyright)),
        }.Where(f => EditFieldConfig.IsVisibleInScene(settings, EditFieldRegistry.FieldOf(f.code)!, scene)).ToList();

        if (fields.Count == 0)
        {
            var placeholder = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", false);
            placeholder.Text = "当前可见性设置下无可用字段（可在「设置 > 编辑字段可见性」中开启）";
            return LyricoUi.Card(placeholder);
        }

        for (var i = 0; i < fields.Count; i++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var i = 0; i < fields.Count; i++)
        {
            var label = ThemeHelper.Label(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
            label.Text = fields[i].label;
            var entry = MakeEntry(fields[i].binding);
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                },
            };
            row.Add(label, 0, 0);
            row.Add(entry, 1, 0);
            grid.Add(row, 0, i);
        }

        return LyricoUi.Card(grid);
    }

    private View BuildCustomTagsCard(IReadOnlyList<string> keys)
    {
        var header = ThemeHelper.Label(15, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
        header.Text = "自定义标签";

        var note = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", false);
        note.Text = "仅对 ID3（MP3）格式文件可写；非 ID3 格式的自定义标签保存不生效。";

        var inner = new VerticalStackLayout { Spacing = 10, Children = { header, note } };

        foreach (var key in keys)
        {
            var keyLabel = ThemeHelper.Label(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
            keyLabel.Text = key;
            keyLabel.VerticalOptions = LayoutOptions.Center;

            var entry = new Entry
            {
                Text = _vm.GetCustomValue(key),
                FontSize = 13,
                TextColor = ThemeHelper.Color("TextPrimaryColor", "#F7F8FF"),
                BackgroundColor = Colors.Transparent,
                Placeholder = key,
                PlaceholderColor = ThemeHelper.Color("TextSecondaryColor", "#C2C6E4"),
            };
            var capturedKey = key;
            entry.TextChanged += (_, e) => _vm.SetCustomValue(capturedKey, e.NewTextValue ?? "");

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                },
            };
            row.Add(keyLabel, 0, 0);
            row.Add(entry, 1, 0);
            inner.Add(row);
        }

        return LyricoUi.Card(inner);
    }

    private static Entry MakeEntry(string binding)
    {
        var e = new Entry
        {
            TextColor = ThemeHelper.Color("TextPrimaryColor", "#F7F8FF"),
            PlaceholderColor = ThemeHelper.Color("TextSecondaryColor", "#C2C6E4"),
            BackgroundColor = Colors.Transparent,
        };
        e.SetBinding(Entry.TextProperty, binding);
        return e;
    }

    private View BuildLyricsCard()
    {
        var title = ThemeHelper.Label(14, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", false);
        title.Text = "歌词（LRC / 纯文本）";
        var editor = new Editor
        {
            HeightRequest = 180,
            TextColor = ThemeHelper.Color("TextPrimaryColor", "#F7F8FF"),
            PlaceholderColor = ThemeHelper.Color("TextSecondaryColor", "#C2C6E4"),
            BackgroundColor = Colors.Transparent,
            Placeholder = "在此粘贴歌词（支持时间轴 LRC）",
        };
        editor.SetBinding(Editor.TextProperty, nameof(EditMetadataViewModel.Lyrics));

        return LyricoUi.Card(new VerticalStackLayout { Spacing = 8, Children = { title, editor } });
    }

    private View BuildSaveRow()
    {
        var status = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        status.SetBinding(Label.TextProperty, nameof(EditMetadataViewModel.StatusText));

        var save = new Button
        {
            Text = "保存到文件",
            FontSize = 15,
            TextColor = Colors.White,
            BackgroundColor = ThemeHelper.Color("PrimaryColor", "#8C7BFF"),
            CornerRadius = 18,
            HeightRequest = 46,
        };
        save.Clicked += async (_, _) =>
        {
            save.IsEnabled = false;
            var ok = await _vm.SaveAsync();
            save.IsEnabled = true;
            if (ok)
            {
                // 提示宿主刷新（改回后可回宿主音乐库看到新标签）
                await DisplayAlert("已保存", "标签已写入音频文件。", "好");
            }
        };

        return new VerticalStackLayout { Spacing = 8, Children = { save, status } };
    }
}
