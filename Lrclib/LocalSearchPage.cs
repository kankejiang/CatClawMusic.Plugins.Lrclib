using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 本地搜索页（Lyrico LocalSearchScreen 复刻）：
/// 顶部搜索框 + 全部/歌曲/专辑/艺人/歌词 五个 pill Tab，
/// 结果用单一 CollectionView ＋ DataTemplateSelector 分区混排（分区头/歌曲/专辑/艺人/歌词命中行）。
/// 点歌曲进详情、点专辑跳专辑详情、点艺人跳艺人详情。
/// </summary>
public class LocalSearchPage : ContentPage
{
    private readonly LocalSearchViewModel _vm;
    private readonly Button[] _tabButtons = new Button[5];
    private readonly CollectionView _list;

    public LocalSearchPage()
    {
        _vm = new LocalSearchViewModel(PluginHost.Library!);
        BindingContext = _vm;

        Title = "本地搜索";
        BackgroundColor = ThemeHelper.Color("WindowBackgroundColor", "#1A1838");

        _list = BuildList();

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
        };
        root.Add(BuildSearchBar(), 0, 0);
        root.Add(BuildTabBar(), 0, 1);
        root.Add(_list, 0, 2);

        Content = root;

        _list.SelectionChanged += OnItemSelected;
        _ = _vm.InitializeCommand.ExecuteAsync(null);
    }

    private async void OnItemSelected(object? sender, SelectionChangedEventArgs e)
    {
        _list.SelectedItem = null; // 允许重复点击同一条
        if (e.CurrentSelection.FirstOrDefault() is not { } item) return;
        switch (item)
        {
            case SongItem s:
                await PluginNav.PushAsync(new SongDetailPage(s));
                break;
            case LyricMatchItem m:
                await PluginNav.PushAsync(new SongDetailPage(m.Song));
                break;
            case AlbumItem a:
                await PluginNav.PushAsync(new AlbumDetailPage(a));
                break;
            case ArtistItem ar:
                await PluginNav.PushAsync(new ArtistDetailPage(ar));
                break;
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_vm.IsLoading && _vm.Results.Count == 0)
            _ = _vm.InitializeCommand.ExecuteAsync(null);
    }

    // ── 顶部搜索框 ──
    private View BuildSearchBar()
    {
        var entry = new Entry
        {
            Placeholder = "搜索歌曲 / 专辑 / 艺人 / 歌词",
            TextColor = ThemeHelper.Color("TextPrimaryColor", "#F7F8FF"),
            PlaceholderColor = ThemeHelper.Color("TextSecondaryColor", "#C2C6E4"),
            BackgroundColor = Colors.Transparent,
            Margin = new Thickness(4, 0),
            VerticalOptions = LayoutOptions.Center,
        };
        entry.SetBinding(Entry.TextProperty, nameof(LocalSearchViewModel.Query));

        return new Border
        {
            HeightRequest = 44,
            Margin = new Thickness(12, 8, 12, 4),
            StrokeThickness = 0,
            Background = ThemeHelper.Brush("CardBackgroundColor", "#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Content = entry,
        };
    }

    // ── 五个 pill Tab ──
    private View BuildTabBar()
    {
        var names = new[] { "全部", "歌曲", "专辑", "艺人", "歌词" };
        var bar = new HorizontalStackLayout
        {
            Spacing = 8,
            Margin = new Thickness(12, 2, 12, 6),
        };
        for (var i = 0; i < names.Length; i++)
        {
            var b = MakePillButton(names[i], i);
            _tabButtons[i] = b;
            bar.Children.Add(b);
        }
        return bar;
    }

    private Button MakePillButton(string text, int index)
    {
        var b = new Button
        {
            Text = text,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 16,
            HeightRequest = 32,
            Padding = new Thickness(14, 0),
            BackgroundColor = ThemeHelper.Color("CardBackgroundColor", "#1AFFFFFF"),
            TextColor = ThemeHelper.Color("TextSecondaryColor", "#C2C6E4"),
            Margin = new Thickness(0, 0, 0, 0),
        };
        b.Clicked += (_, _) => SetActiveTab(index);
        return b;
    }

    private void SetActiveTab(int index)
    {
        _vm.ActiveTab = index;
        for (var i = 0; i < _tabButtons.Length; i++)
        {
            var selected = i == index;
            _tabButtons[i].BackgroundColor = selected
                ? ThemeHelper.Color("PrimaryColor", "#8C7BFF")
                : ThemeHelper.Color("CardBackgroundColor", "#1AFFFFFF");
            _tabButtons[i].TextColor = selected
                ? Colors.White
                : ThemeHelper.Color("TextSecondaryColor", "#C2C6E4");
        }
    }

    // ── 分区混排列表（DataTemplateSelector） ──
    private static CollectionView BuildList()
    {
        var list = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            Margin = new Thickness(0, 0, 0, 0),
            ItemTemplate = new LocalSearchTemplateSelector
            {
                SectionHeaderTemplate = new DataTemplate(BuildSectionHeader),
                SongTemplate = new DataTemplate(BuildSongRow),
                AlbumTemplate = new DataTemplate(BuildAlbumRow),
                ArtistTemplate = new DataTemplate(BuildArtistRow),
                LyricTemplate = new DataTemplate(BuildLyricRow),
                AlbumDetailTemplate = new DataTemplate(BuildAlbumRow),
            },
        };
        list.SetBinding(ItemsView.ItemsSourceProperty, nameof(LocalSearchViewModel.Results));
        return list;
    }

    private static object BuildSectionHeader()
    {
        var title = ThemeHelper.Label(13, FontAttributes.Bold, "PrimaryColor", "#8C7BFF", false);
        title.SetBinding(Label.TextProperty, nameof(SectionHeaderItem.Title));
        var subtitle = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", false);
        subtitle.SetBinding(Label.TextProperty, nameof(SectionHeaderItem.Subtitle));

        var grid = new Grid
        {
            Padding = new Thickness(16, 12, 16, 6),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };
        grid.Add(title, 0);
        grid.Add(subtitle, 1);
        return grid;
    }

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

    private static object BuildAlbumRow()
    {
        var title = ThemeHelper.Label(15, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
        title.SetBinding(Label.TextProperty, nameof(AlbumItem.Title));
        var subtitle = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        subtitle.SetBinding(Label.TextProperty, nameof(AlbumItem.Subtitle));

        var textStack = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Spacing = 2,
            Children = { title, subtitle },
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
        grid.Add(BuildCover(nameof(AlbumItem.CoverPath), nameof(AlbumItem.CoverText), 48), 0);
        grid.Add(textStack, 1);
        return grid;
    }

    private static object BuildArtistRow()
    {
        var name = ThemeHelper.Label(15, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
        name.SetBinding(Label.TextProperty, nameof(ArtistItem.Name));
        var subtitle = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        subtitle.SetBinding(Label.TextProperty, nameof(ArtistItem.Subtitle));

        var textStack = new VerticalStackLayout
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
        grid.Add(textStack, 1);
        return grid;
    }

    private static object BuildLyricRow()
    {
        var title = ThemeHelper.Label(15, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
        title.SetBinding(Label.TextProperty, nameof(LyricMatchItem.Song.Title));
        var artist = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        artist.SetBinding(Label.TextProperty, nameof(LyricMatchItem.Song.Artist));
        var lyric = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", false);
        lyric.MaxLines = 1;
        lyric.SetBinding(Label.TextProperty, nameof(LyricMatchItem.LyricLine));

        var textStack = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Spacing = 2,
            Children = { title, artist, lyric },
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
        grid.Add(BuildCover(nameof(LyricMatchItem.Song.CoverPath), nameof(LyricMatchItem.Song.CoverText), 48), 0);
        grid.Add(textStack, 1);
        return grid;
    }

    // ── 封面占位（含图显示图，无图显示首字） ──
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
        placeholderLabel.SetBinding(Label.TextProperty, new Binding(coverTextBinding) { Converter = new FirstCharConverter() });

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
}

/// <summary>
/// 分区模板选择器：按行类型返回对应 DataTemplate，并用 SelectionChanged 之外的方式导航。
/// 导航在页面订阅 CollectionView.SelectionChanged 完成（此处仅提供模板）。
/// </summary>
internal class LocalSearchTemplateSelector : DataTemplateSelector
{
    public DataTemplate SectionHeaderTemplate { get; set; } = null!;
    public DataTemplate SongTemplate { get; set; } = null!;
    public DataTemplate AlbumTemplate { get; set; } = null!;
    public DataTemplate ArtistTemplate { get; set; } = null!;
    public DataTemplate LyricTemplate { get; set; } = null!;
    public DataTemplate AlbumDetailTemplate { get; set; } = null!;

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) => item switch
    {
        SectionHeaderItem => SectionHeaderTemplate,
        SongItem => SongTemplate,
        LyricMatchItem => LyricTemplate,
        AlbumItem => AlbumTemplate,
        ArtistItem => ArtistTemplate,
        _ => AlbumDetailTemplate,
    };
}