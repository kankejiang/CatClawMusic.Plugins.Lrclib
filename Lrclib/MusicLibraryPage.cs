using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using System.ComponentModel;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// Lyrico 风格音乐库主页（纯 C# 构建）：
/// 顶部搜索框 + 歌曲/专辑/艺人三大 Tab + 右侧字母索引侧栏 + 深色卡片列表/网格。
/// 数据复用宿主已扫描的本地音乐库（<see cref="IMusicLibraryService"/>）。
/// </summary>
public class MusicLibraryPage : ContentPage
{
    private readonly MusicLibraryViewModel _vm;
    private CollectionView? _songsView;
    private CollectionView? _albumsView;
    private CollectionView? _artistsView;
    private readonly ContentView _contentHost = new();
    private readonly ActivityIndicator _loadingIndicator = new();
    private readonly Button _tabSongs;
    private readonly Button _tabAlbums;
    private readonly Button _tabArtists;
    private readonly VerticalStackLayout _letterRail;
    private readonly Label _selectionCountLabel;
    private readonly Border _batchBar;
    private bool _selectionMode;

    public MusicLibraryPage(MusicLibraryViewModel vm)
    {
        _vm = vm;
        BindingContext = _vm;

        Title = "Lyrico";
        BackgroundColor = ThemeHelper.Color("WindowBackgroundColor", "#1A1838");

        var searchBar = BuildSearchBar();
        (_tabSongs, _tabAlbums, _tabArtists) = BuildBottomTabs();
        _letterRail = new VerticalStackLayout
        {
            Spacing = 2,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalOptions = LayoutOptions.Center,
        };

        var contentArea = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        _loadingIndicator.IsRunning = true;
        _loadingIndicator.VerticalOptions = LayoutOptions.Center;
        _loadingIndicator.HorizontalOptions = LayoutOptions.Center;
        _loadingIndicator.SetDynamicResource(ActivityIndicator.ColorProperty, "PrimaryColor");
        contentArea.Add(_contentHost, 0);
        contentArea.Add(_loadingIndicator, 0);
        contentArea.Add(_letterRail, 1);

        // 批量操作栏（多选模式时显示在底部 Tab 上方）：
        // 必须先初始化 _selectionCountLabel，BuildBatchBar() 内部会 Add 它，否则构造抛 NRE。
        _selectionCountLabel = new Label
        {
            FontSize = 13,
            VerticalOptions = LayoutOptions.Center,
        };
        _selectionCountLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");

        _batchBar = BuildBatchBar();

        var bottomArea = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
        };
        bottomArea.Add(_batchBar, 0);
        bottomArea.Add(BuildTabBar(), 1);
        _batchBar.IsVisible = false;

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
        };
        root.Add(searchBar, 0);
        root.Add(contentArea, 1);
        root.Add(bottomArea, 2);

        Content = root;

        _vm.PropertyChanged += OnVmPropertyChanged;
        SetActiveTab(_vm.ActiveTab);
    }

    // ── 批量操作栏 ──
    private Border BuildBatchBar()
    {
        var bar = new Grid
        {
            Padding = new Thickness(12, 6, 12, 4),
            RowSpacing = 8,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
        };

        bar.Add(_selectionCountLabel, 0, 0);

        var batchLyrics = MakeBatchButton("批量歌词", async () =>
        {
            var songs = GetSelectedSongs();
            if (songs.Count == 0) return;
            await PluginNav.PushAsync(new BatchOperationsPage(songs, BatchOperationMode.MatchLyrics));
        });
        var batchEdit = MakeBatchButton("批量编辑", async () =>
        {
            var songs = GetSelectedSongs();
            if (songs.Count == 0) return;
            await PluginNav.PushAsync(new BatchOperationsPage(songs, BatchOperationMode.EditTags));
        });
        var batchRename = MakeBatchButton("批量重命名", async () =>
        {
            var songs = GetSelectedSongs();
            if (songs.Count == 0) return;
            await PluginNav.PushAsync(new BatchOperationsPage(songs, BatchOperationMode.RenameFiles));
        });
        var batchDelete = MakeBatchButton("批量删除", async () =>
        {
            var songs = GetSelectedSongs();
            if (songs.Count == 0) return;
            var confirm = await Shell.Current?.DisplayAlert("删除确认",
                $"确定删除选中的 {songs.Count} 首文件吗？此操作不可恢复。", "删除", "取消");
            if (confirm == true)
                await PluginNav.PushAsync(new BatchOperationsPage(songs, BatchOperationMode.DeleteFiles));
        });
        var batchFormat = MakeBatchButton("批量歌词格式", async () =>
        {
            var songs = GetSelectedSongs();
            if (songs.Count == 0) return;
            await PluginNav.PushAsync(new BatchLyricsFormatPage(songs));
        });
        var batchTag = MakeBatchButton("批量标签", async () =>
        {
            var songs = GetSelectedSongs();
            if (songs.Count == 0) return;
            await PluginNav.PushAsync(new BatchTagTransferPage(songs));
        });
        var batchLoudness = MakeBatchButton("批量响度", async () =>
        {
            var songs = GetSelectedSongs();
            if (songs.Count == 0) return;
            await PluginNav.PushAsync(new BatchReplayGainPage(songs));
        });

        var btnRow = new FlexLayout
        {
            Direction = FlexDirection.Row,
            Wrap = FlexWrap.Wrap,
            JustifyContent = FlexJustify.Start,
        };
        btnRow.Children.Add(batchLyrics);
        btnRow.Children.Add(batchEdit);
        btnRow.Children.Add(batchRename);
        btnRow.Children.Add(batchDelete);
        btnRow.Children.Add(batchFormat);
        btnRow.Children.Add(batchTag);
        btnRow.Children.Add(batchLoudness);
        bar.Add(btnRow, 0, 1);

        return new Border
        {
            StrokeThickness = 0,
            Background = ThemeHelper.Brush("CardBackgroundColor", "#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(0, 0, 0, 0) },
            Content = bar,
        };
    }

    private static Button MakeBatchButton(string text, Func<Task> onClick)
    {
        var b = new Button
        {
            Text = text,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = ThemeHelper.Color("PrimaryColor", "#8C7BFF"),
            CornerRadius = 16,
            Padding = new Thickness(14, 2),
            HeightRequest = 34,
        };
        b.Clicked += async (_, _) => await onClick();
        return b;
    }

    /// <summary>当前已选中的歌曲（多选模式）</summary>
    private List<SongItem> GetSelectedSongs()
        => _songsView?.SelectedItems?.Cast<SongItem>().ToList() ?? new List<SongItem>();

    /// <summary>切换多选模式：歌曲列表进入多选，显示批量操作栏</summary>
    private void ToggleSelectionMode()
    {
        _selectionMode = !_selectionMode;

        if (_songsView != null)
        {
            if (_selectionMode)
            {
                _songsView.SelectionMode = SelectionMode.Multiple;
                _batchBar.IsVisible = true;
                _letterRail.IsVisible = false;
            }
            else
            {
                _songsView.SelectionMode = SelectionMode.Single;
                _songsView.SelectedItems = null;
                _batchBar.IsVisible = false;
            }
        }

        UpdateSelectionCount();
    }

    private void UpdateSelectionCount()
    {
        var n = _songsView?.SelectedItems?.Count ?? 0;
        _selectionCountLabel.Text = _selectionMode ? $"已选 {n} 首" : "";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // 延迟到 Push 动画结束后再加载，避免导航动画期间与数据加载争抢 UI 线程导致 ANR。
        if (!_vm.IsLoading && _vm.SongGroups.Count == 0)
        {
            try { await Task.Delay(150); } catch { }
            _vm.LoadCommand.Execute(null);
        }
    }

    // ── 顶部搜索框 + 多选入口 ──
    private View BuildSearchBar()
    {
        var entry = new Entry
        {
            Placeholder = "搜索歌曲 / 专辑 / 艺人",
            TextColor = ThemeHelper.Color("TextPrimaryColor", "#F7F8FF"),
            PlaceholderColor = ThemeHelper.Color("TextSecondaryColor", "#C2C6E4"),
            BackgroundColor = Colors.Transparent,
            Margin = new Thickness(4, 0),
            VerticalOptions = LayoutOptions.Center,
        };
        entry.SetBinding(Entry.TextProperty, nameof(MusicLibraryViewModel.SearchQuery));

        var selectButton = new Button
        {
            Text = "选择",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = ThemeHelper.Color("PrimaryColor", "#8C7BFF"),
            CornerRadius = 16,
            Padding = new Thickness(14, 2),
            HeightRequest = 34,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        selectButton.Clicked += (_, _) => ToggleSelectionMode();

        var searchPageButton = new Button
        {
            Text = "搜索",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeHelper.Color("PrimaryColor", "#8C7BFF"),
            BackgroundColor = Colors.Transparent,
            CornerRadius = 16,
            Padding = new Thickness(14, 2),
            HeightRequest = 34,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        searchPageButton.Clicked += async (_, _) => await PluginNav.PushAsync(new LocalSearchPage());

        var settingsButton = new Button
        {
            Text = "设置",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeHelper.Color("TextSecondaryColor", "#C2C6E4"),
            BackgroundColor = Colors.Transparent,
            CornerRadius = 16,
            Padding = new Thickness(14, 2),
            HeightRequest = 34,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        settingsButton.Clicked += async (_, _) => await PluginNav.PushAsync(new EditorSettingsPage());

        var bar = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        bar.Add(entry, 0);
        bar.Add(searchPageButton, 1);
        bar.Add(selectButton, 2);
        bar.Add(settingsButton, 3);

        return new Border
        {
            HeightRequest = 44,
            Margin = new Thickness(12, 8, 12, 4),
            StrokeThickness = 0,
            Background = ThemeHelper.Brush("CardBackgroundColor", "#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Content = bar,
        };
    }

    // ── 三个 Tab 的内容视图（懒加载，切到时才创建）──
    private CollectionView BuildSongsView()
    {
        var songs = new CollectionView
        {
            IsGrouped = true,
            SelectionMode = SelectionMode.Single,
            Margin = new Thickness(0, 4, 0, 0),
        };
        songs.SetBinding(ItemsView.ItemsSourceProperty, nameof(MusicLibraryViewModel.SongGroups));
        songs.GroupHeaderTemplate = new DataTemplate(BuildSongHeader);
        songs.ItemTemplate = new DataTemplate(BuildSongRow);
        songs.SelectionChanged += OnSongSelected;
        return songs;
    }

    private CollectionView BuildAlbumsView()
    {
        var albums = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            Margin = new Thickness(12, 4, 12, 0),
        };
        albums.SetBinding(ItemsView.ItemsSourceProperty, nameof(MusicLibraryViewModel.Albums));
        albums.ItemsLayout = new GridItemsLayout(2, ItemsLayoutOrientation.Vertical)
        {
            HorizontalItemSpacing = 10,
            VerticalItemSpacing = 12,
        };
        albums.ItemTemplate = new DataTemplate(BuildAlbumCard);
        albums.SelectionChanged += OnAlbumSelected;
        return albums;
    }

    private CollectionView BuildArtistsView()
    {
        var artists = new CollectionView
        {
            IsGrouped = true,
            SelectionMode = SelectionMode.Single,
            Margin = new Thickness(0, 4, 0, 0),
        };
        artists.SetBinding(ItemsView.ItemsSourceProperty, nameof(MusicLibraryViewModel.ArtistGroups));
        artists.GroupHeaderTemplate = new DataTemplate(BuildArtistHeader);
        artists.ItemTemplate = new DataTemplate(BuildArtistRow);
        artists.SelectionChanged += OnArtistSelected;
        return artists;
    }

    // ── 歌曲分组头：字母 + 数量 ──
    private static object BuildSongHeader()
    {
        var grid = new Grid
        {
            Padding = new Thickness(16, 10, 16, 6),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };
        var key = ThemeHelper.Label(14, FontAttributes.Bold, "PrimaryColor", "#8C7BFF", true);
        key.SetBinding(Label.TextProperty, nameof(SongGroup.Key));
        var count = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        count.SetBinding(Label.TextProperty, nameof(SongGroup.CountText));
        grid.Add(key, 0);
        grid.Add(count, 1);
        return grid;
    }

    // ── 歌曲行 ──
    private static object BuildSongRow()
    {
        var title = ThemeHelper.Label(15, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
        title.SetBinding(Label.TextProperty, nameof(SongItem.Title));
        var artist = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        artist.SetBinding(Label.TextProperty, nameof(SongItem.Artist));
        var duration = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", false);
        duration.SetBinding(Label.TextProperty, nameof(SongItem.DurationText));

        var textStack = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Spacing = 2,
            Children = { title, artist },
        };

        var grid = new Grid
        {
            Padding = new Thickness(12, 6),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        grid.Add(BuildCover(nameof(SongItem.CoverPath), nameof(SongItem.CoverText), 48), 0);
        grid.Add(textStack, 1);
        grid.Add(duration.CenteredY(), 2);
        return grid;
    }

    // ── 专辑卡片（两列网格） ──
    private static object BuildAlbumCard()
    {
        var title = ThemeHelper.Label(14, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
        title.SetBinding(Label.TextProperty, nameof(AlbumItem.Title));
        var subtitle = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        subtitle.SetBinding(Label.TextProperty, nameof(AlbumItem.Subtitle));

        var stack = new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                BuildCover(nameof(AlbumItem.CoverPath), nameof(AlbumItem.CoverText), 120, 12).CenterH(),
                title,
                subtitle,
            },
        };

        return new Border
        {
            StrokeThickness = 0,
            Background = ThemeHelper.Brush("CardBackgroundColor", "#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Padding = new Thickness(10),
            Content = stack,
        };
    }

    // ── 艺人分组头 ──
    private static object BuildArtistHeader()
    {
        var grid = new Grid
        {
            Padding = new Thickness(16, 10, 16, 6),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };
        var key = ThemeHelper.Label(14, FontAttributes.Bold, "PrimaryColor", "#8C7BFF", true);
        key.SetBinding(Label.TextProperty, nameof(ArtistGroup.Key));
        var count = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        count.SetBinding(Label.TextProperty, nameof(ArtistGroup.CountText));
        grid.Add(key, 0);
        grid.Add(count, 1);
        return grid;
    }

    // ── 艺人行 ──
    private static object BuildArtistRow()
    {
        var name = ThemeHelper.Label(15, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
        name.SetBinding(Label.TextProperty, nameof(ArtistItem.Name));
        var subtitle = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        subtitle.SetBinding(Label.TextProperty, nameof(ArtistItem.Subtitle));

        var stack = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Spacing = 2,
            Children = { name, subtitle },
        };

        var grid = new Grid
        {
            Padding = new Thickness(12, 6),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };
        grid.Add(BuildCover(null, nameof(ArtistItem.CoverText), 48), 0);
        grid.Add(stack, 1);
        return grid;
    }

    // ── 封面（有图显示图，无图显示首字占位） ──
    private static View BuildCover(string? coverPathBinding, string coverTextBinding, double size, double corner = 10)
    {
        var image = new Image
        {
            HeightRequest = size,
            WidthRequest = size,
            Aspect = Aspect.AspectFill,
        };
        if (coverPathBinding != null)
        {
            image.SetBinding(Image.SourceProperty, new Binding(coverPathBinding, converter: new CoverSourceConverter()));
            image.SetBinding(VisualElement.IsVisibleProperty, new Binding(coverPathBinding, converter: new HasValueToVisibleConverter()));
        }
        else
        {
            image.IsVisible = false;
        }

        var placeholderLabel = new Label
        {
            FontSize = size * 0.36,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        placeholderLabel.SetDynamicResource(Label.TextColorProperty, "PrimaryColor");
        placeholderLabel.SetBinding(Label.TextProperty, new Binding(coverTextBinding)
        {
            Converter = new FirstCharConverter(),
        });

        var placeholder = new Border
        {
            HeightRequest = size,
            WidthRequest = size,
            StrokeThickness = 0,
            Background = ThemeHelper.Brush("CardBackgroundStrongColor", "#2DFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(corner) },
            Content = placeholderLabel,
        };
        if (coverPathBinding != null)
            placeholder.SetBinding(VisualElement.IsVisibleProperty, new Binding(coverPathBinding, converter: new EmptyToVisibleConverter()));

        return new Grid
        {
            HeightRequest = size,
            WidthRequest = size,
            Children = { placeholder, image },
        };
    }

    // ── 底部 Tab ──
    private (Button songs, Button albums, Button artists) BuildBottomTabs()
        => (MakeTab("歌曲"), MakeTab("专辑"), MakeTab("艺人"));

    private static Button MakeTab(string text) => new()
    {
        Text = text,
        FontSize = 14,
        FontAttributes = FontAttributes.Bold,
        BackgroundColor = Colors.Transparent,
        CornerRadius = 18,
        HeightRequest = 40,
    };

    private View BuildTabBar()
    {
        _tabSongs.Clicked += (_, _) => SetActiveTab(0);
        _tabAlbums.Clicked += (_, _) => SetActiveTab(1);
        _tabArtists.Clicked += (_, _) => SetActiveTab(2);

        var bar = new Grid
        {
            Padding = new Thickness(12, 6, 12, 8),
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
        };
        bar.Add(_tabSongs, 0);
        bar.Add(_tabAlbums, 1);
        bar.Add(_tabArtists, 2);
        return bar;
    }

    // ── 字母索引侧栏 ──
    private void RebuildLetterRail()
    {
        _letterRail.Children.Clear();
        var letters = _vm.ActiveTab switch
        {
            0 => _vm.SongLetters,
            2 => _vm.ArtistLetters,
            _ => null,
        };
        if (letters == null || letters.Count == 0)
        {
            _letterRail.IsVisible = false;
            return;
        }

        foreach (var letter in letters)
        {
            var label = ThemeHelper.Label(11, FontAttributes.Bold, "TextSecondaryColor", "#C2C6E4", false);
            label.Text = letter.Key;
            label.Padding = new Thickness(2, 1);
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, e) => OnLetterTapped(letter.Key);
            label.GestureRecognizers.Add(tap);
            _letterRail.Children.Add(label);
        }
        _letterRail.IsVisible = true;
    }

    private void OnLetterTapped(string letter)
    {
        if (_vm.ActiveTab == 0 && _songsView != null)
        {
            var gi = IndexOf(_vm.SongGroups, letter, g => g.Key);
            if (gi >= 0) _songsView.ScrollTo(0, gi, ScrollToPosition.Start, false);
        }
        else if (_vm.ActiveTab == 2 && _artistsView != null)
        {
            var gi = IndexOf(_vm.ArtistGroups, letter, g => g.Key);
            if (gi >= 0) _artistsView.ScrollTo(0, gi, ScrollToPosition.Start, false);
        }
    }

    private static int IndexOf<T>(IReadOnlyList<T> groups, string key, Func<T, string> keySelector)
    {
        for (var i = 0; i < groups.Count; i++)
            if (keySelector(groups[i]) == key) return i;
        return -1;
    }

    // ── Tab 切换 ──
    private void SetActiveTab(int tab)
    {
        if (_vm.ActiveTab != tab) _vm.ActiveTab = tab;

        // 懒加载：进入时只创建歌曲视图，切到其他 Tab 时才创建，避免构造期同时创建 3 个
        // CollectionView（其中 2 个 IsGrouped）在主线程造成 ANR。
        _contentHost.Content = tab switch
        {
            0 => _songsView ??= BuildSongsView(),
            1 => _albumsView ??= BuildAlbumsView(),
            2 => _artistsView ??= BuildArtistsView(),
            _ => _contentHost.Content,
        };

        var primary = ThemeHelper.Color("PrimaryColor", "#8C7BFF");
        var secondary = ThemeHelper.Color("TextSecondaryColor", "#C2C6E4");

        _tabSongs.TextColor = tab == 0 ? Colors.White : secondary;
        _tabSongs.BackgroundColor = tab == 0 ? primary : Colors.Transparent;
        _tabAlbums.TextColor = tab == 1 ? Colors.White : secondary;
        _tabAlbums.BackgroundColor = tab == 1 ? primary : Colors.Transparent;
        _tabArtists.TextColor = tab == 2 ? Colors.White : secondary;
        _tabArtists.BackgroundColor = tab == 2 ? primary : Colors.Transparent;

        RebuildLetterRail();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MusicLibraryViewModel.ActiveTab))
            SetActiveTab(_vm.ActiveTab);
        else if (e.PropertyName == nameof(MusicLibraryViewModel.SongLetters)
              || e.PropertyName == nameof(MusicLibraryViewModel.ArtistLetters))
            RebuildLetterRail();
        else if (e.PropertyName == nameof(MusicLibraryViewModel.IsLoading))
            _loadingIndicator.IsVisible = _vm.IsLoading;
    }

    // ── 选择跳转 ──
    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_selectionMode)
        {
            UpdateSelectionCount();
            return;
        }
        if (e.CurrentSelection.FirstOrDefault() is not SongItem song) return;
        if (_songsView != null) _songsView.SelectedItem = null;
        await PluginNav.PushAsync(new SongDetailPage(song));
    }

    private async void OnAlbumSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not AlbumItem album) return;
        if (_albumsView != null) _albumsView.SelectedItem = null;
        await PluginNav.PushAsync(new AlbumDetailPage(album));
    }

    private async void OnArtistSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ArtistItem artist) return;
        if (_artistsView != null) _artistsView.SelectedItem = null;
        await PluginNav.PushAsync(new ArtistDetailPage(artist));
    }
}

/// <summary>取字符串首字符（大写），空返回 ♪（封面占位用）</summary>
internal class FirstCharConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var s = value as string;
        if (string.IsNullOrWhiteSpace(s)) return "♪";
        var c = s.Trim()[0];
        return char.IsAsciiLetter(c) ? char.ToUpperInvariant(c).ToString() : c.ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
