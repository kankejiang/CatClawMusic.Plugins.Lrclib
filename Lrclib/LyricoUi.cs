using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>Lyrico 风格共享 UI 构建器：歌曲行 / 封面 / 操作按钮</summary>
internal static class LyricoUi
{
    /// <summary>歌曲行：封面 + 标题/艺人 + 时长（绑定 SongItem）</summary>
    public static View SongRow()
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
        grid.Add(Cover(nameof(SongItem.CoverPath), nameof(SongItem.CoverText), 48), 0);
        grid.Add(textStack, 1);
        grid.Add(duration.CenteredY(), 2);
        return grid;
    }

    /// <summary>封面：有本地图显示图，无图显示首字占位</summary>
    public static View Cover(string? coverPathBinding, string coverTextBinding, double size, double corner = 10)
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

    /// <summary>主题色圆角操作按钮</summary>
    public static Button ActionButton(string text, Action onClick)
    {
        var b = new Button
        {
            Text = text,
            FontSize = 13,
            TextColor = Colors.White,
            BackgroundColor = ThemeHelper.Color("PrimaryColor", "#8C7BFF"),
            CornerRadius = 16,
            HeightRequest = 44,
        };
        b.Clicked += (_, _) => onClick();
        return b;
    }

    /// <summary>深色卡片容器</summary>
    public static Border Card(View content, double corner = 16)
        => new()
        {
            StrokeThickness = 0,
            Background = ThemeHelper.Brush("CardBackgroundColor", "#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(corner) },
            Padding = new Thickness(14),
            Content = content,
        };
}
