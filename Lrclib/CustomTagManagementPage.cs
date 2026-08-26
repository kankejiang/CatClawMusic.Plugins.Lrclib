using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 自定义标签管理页（复刻 Lyrico <c>CustomTagManagementScreen</c>）：
/// 维护「自定义标签可见键」列表——用户可新增/移除想在编辑页显示的自定义标签键（如 TXXX 帧键）。
/// 键名大写规范化、去重、≤64 字符、不含换行；改动立即持久化。
/// <para>说明：宿主已支持读写 ID3（MP3）格式的 TXXX 自定义标签帧；在「编辑标签」页将按可见键渲染对应输入项，
/// 非 ID3 格式文件的自定义标签保存不生效。</para>
/// </summary>
public class CustomTagManagementPage : ContentPage
{
    private readonly EditorSettingsStore _store = new();
    private VerticalStackLayout? _list;
    private Entry? _input;
    private Label? _error;

    public CustomTagManagementPage()
    {
        Title = "自定义标签";
        BackgroundColor = ThemeHelper.Color("WindowBackgroundColor", "#1A1838");
        Content = new ScrollView { Content = BuildContent() };
        WideAdapt.Attach(this, WideAdapt.FormMaxWidth);
    }

    private View BuildContent()
    {
        var stack = new VerticalStackLayout { Spacing = 10, Padding = new Thickness(12, 8) };
        stack.Add(BuildHint());

        _input = new Entry
        {
            Placeholder = "输入标签键名后回车，如 BYEAR",
            TextColor = ThemeHelper.Color("TextPrimaryColor", "#F7F8FF"),
            PlaceholderColor = ThemeHelper.Color("TextSecondaryColor", "#C2C6E4"),
            BackgroundColor = Colors.Transparent,
        };
        _input.Completed += (_, _) => AddKey();

        var addButton = LyricoUi.ActionButton("添加", AddKey);

        var inputRow = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        inputRow.Add(_input, 0);
        inputRow.Add(addButton, 1);

        _error = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", false);
        _error.IsVisible = false;

        stack.Add(ThemeHelper.Card(new VerticalStackLayout
        {
            Spacing = 8,
            Children = { inputRow, _error },
        }));

        _list = new VerticalStackLayout { Spacing = 8 };
        stack.Add(ThemeHelper.Card(_list, corner: 16));
        stack.Add(BuildResetButton());
        RefreshList();
        return stack;
    }

    private View BuildHint()
        => ThemeHelper.Card(new Label
        {
            Text = "新增想在编辑页显示的自定义标签键。键名自动转为大写；同一键仅保留一次。",
            FontSize = 12,
            LineBreakMode = LineBreakMode.WordWrap,
            TextColor = ThemeHelper.Color("TextSecondaryColor", "#C2C6E4"),
        });

    private void AddKey()
    {
        if (_input == null || _error == null) return;
        var input = _input.Text ?? "";
        var key = _store.AddCustomVisibleKey(input);
        if (key == null)
        {
            _error.Text = "无效的键名：需非空、≤64 字符且不含换行。";
            _error.IsVisible = true;
            return;
        }
        _error.IsVisible = false;
        _input.Text = "";
        RefreshList();
    }

    private void RemoveKey(string key)
    {
        _store.RemoveCustomVisibleKey(key);
        RefreshList();
    }

    private void RefreshList()
    {
        if (_list == null) return;
        _list.Children.Clear();
        var keys = _store.Get().CustomVisibleKeys;
        if (keys.Count == 0)
        {
            var empty = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", false);
            empty.Text = "暂无自定义标签键";
            empty.HorizontalOptions = LayoutOptions.Center;
            _list.Children.Add(empty);
            return;
        }

        foreach (var key in keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var name = ThemeHelper.Label(13, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
            name.Text = key;

            var del = new Button
            {
                Text = "移除",
                FontSize = 12,
                TextColor = ThemeHelper.Color("TextSecondaryColor", "#C2C6E4"),
                BackgroundColor = Colors.Transparent,
                CornerRadius = 12,
                Padding = new Thickness(10, 0),
                HeightRequest = 30,
                VerticalOptions = LayoutOptions.Center,
            };
            del.Clicked += (_, _) => RemoveKey(key);

            var row = new Grid
            {
                ColumnSpacing = 10,
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                },
            };
            row.Add(name, 0);
            row.Add(del, 1);
            _list.Children.Add(row);
        }
    }

    private View BuildResetButton()
    {
        var reset = new Button
        {
            Text = "清空全部",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeHelper.Color("TextSecondaryColor", "#C2C6E4"),
            BackgroundColor = Colors.Transparent,
            CornerRadius = 16,
            HeightRequest = 44,
        };
        reset.Clicked += async (_, _) =>
        {
            var ok = await DisplayAlert("清空全部", "确定移除全部自定义标签键吗？", "清空", "取消");
            if (!ok) return;
            _store.Save(new EditorSettings { FieldOverrides = _store.Get().FieldOverrides });
            RefreshList();
        };
        return reset;
    }
}