using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>艺人详情页：艺人名 + 歌曲数量 + 该艺人的歌曲列表（点歌曲进详情）</summary>
public class ArtistDetailPage : ContentPage
{
    private readonly ArtistItem _artist;
    private readonly CollectionView _list = new()
    {
        SelectionMode = SelectionMode.Single,
    };

    public ArtistDetailPage(ArtistItem artist)
    {
        _artist = artist;
        BindingContext = artist;   // 页头绑定依赖（Name/Subtitle/CoverText）
        Title = artist.Name;
        BackgroundColor = ThemeHelper.Color("WindowBackgroundColor", "#1A1838");

        _list.ItemTemplate = new DataTemplate(LyricoUi.SongRow);
        _list.SelectionChanged += OnSongSelected;

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
        };
        root.Add(BuildHeader(), 0, 0);
        root.Add(_list, 0, 1);
        Content = root;
        WideAdapt.Attach(this);
        KickOffLoad();
    }

    private bool _loaded;

    /// <summary>构造期即启动加载：桌面模态导航 WrapRoot 会吞掉首推页的 OnAppearing，不能依赖它触发。</summary>
    private void KickOffLoad()
    {
        if (_loaded) return;
        _loaded = true;
        _ = LoadAsync();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        KickOffLoad();
    }

    private async Task LoadAsync()
    {
        var library = PluginHost.Library;
        if (library is null) return;

        var songs = await library.GetSongsByArtistAsync(_artist.Name);
        var items = songs
            .Where(s => s.Source == CatClawMusic.Core.Models.SongSource.Local)
            .Select(s => new SongItem(s))
            .ToList();
        _list.ItemsSource = items;
    }

    private View BuildHeader()
    {
        var name = ThemeHelper.Label(20, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
        name.SetBinding(Label.TextProperty, nameof(ArtistItem.Name));
        var subtitle = ThemeHelper.Label(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        subtitle.SetBinding(Label.TextProperty, nameof(ArtistItem.Subtitle));

        var placeholderLabel = new Label
        {
            FontSize = 56,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        placeholderLabel.SetDynamicResource(Label.TextColorProperty, "PrimaryColor");
        placeholderLabel.SetBinding(Label.TextProperty, new Binding(nameof(ArtistItem.CoverText))
        {
            Converter = new FirstCharConverter(),
        });

        var cover = new Border
        {
            HeightRequest = 180,
            WidthRequest = 180,
            StrokeThickness = 0,
            Background = ThemeHelper.Brush("CardBackgroundStrongColor", "#2DFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20) },
            Content = placeholderLabel,
        };

        var text = new VerticalStackLayout
        {
            Spacing = 6,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children = { name, subtitle },
        };

        // 宽屏（Windows/横屏）封面居左、文字居右；窄屏封面上、文字下居中
        var header = new Grid
        {
            Padding = new Thickness(16, 16, 16, 12),
            RowSpacing = 10,
            ColumnSpacing = 20,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };
        header.Add(cover, 0, 0);
        Grid.SetColumnSpan(cover, 2);
        header.Add(text, 0, 1);
        Grid.SetColumnSpan(text, 2);
        WideAdapt.AttachHeader(this, header, cover, text);
        return header;
    }

    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SongItem song) return;
        _list.SelectedItem = null;
        await PluginNav.PushAsync(new EditMetadataPage(song));
    }
}
