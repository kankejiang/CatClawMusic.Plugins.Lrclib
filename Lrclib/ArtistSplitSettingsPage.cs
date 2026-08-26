using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 艺术家拆分设置页（复刻 Lyrico <c>ArtistSplitSettingsScreen</c>）：
/// 配置艺人库如何拆分多艺人——启用开关、内置分隔符开关、自定义分隔符增删、不拆分艺人名单、
/// 恢复默认；改动即写盘并触发艺人库重建。
/// </summary>
public class ArtistSplitSettingsPage : ContentPage
{
    private readonly ArtistSplitStore _store = new();
    private VerticalStackLayout? _customSepList;
    private VerticalStackLayout? _noSplitList;
    private Entry? _sepInput;
    private Entry? _noSplitInput;

    public ArtistSplitSettingsPage()
    {
        Title = "艺术家拆分设置";
        BackgroundColor = ThemeHelper.Color("WindowBackgroundColor", "#1A1838");
        Content = new ScrollView { Content = BuildContent() };
        WideAdapt.Attach(this, WideAdapt.FormMaxWidth);
    }

    private View BuildContent()
    {
        var stack = new VerticalStackLayout { Spacing = 10, Padding = new Thickness(12, 8) };
        var config = _store.Get();

        stack.Add(BuildEnableCard(config));
        stack.Add(BuildHint());
        stack.Add(BuildSeparatorCard(config));
        stack.Add(BuildNoSplitCard(config));
        stack.Add(BuildResetButton());
        return stack;
    }

    private View BuildEnableCard(ArtistSplitConfig config)
    {
        var sw = new Switch { IsToggled = config.Enabled, VerticalOptions = LayoutOptions.Center };
        sw.Toggled += (_, e) => { config.Enabled = e.Value; _store.Save(config); };

        var title = ThemeHelper.Label(15, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
        title.Text = "启用艺人拆分";

        var sub = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        sub.Text = "按分隔符把多艺人拆开，便于艺人库单独归组";

        var left = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Spacing = 2,
            Children = { title, sub },
        };

        var grid = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        grid.Add(left, 0);
        grid.Add(sw, 1);
        return ThemeHelper.Card(grid);
    }

    private View BuildHint()
        => ThemeHelper.Card(new Label
        {
            Text = "分隔符内的“/”“；”“，”等会拆分多艺人；被列入“不拆分艺人”的整名不会拆开。改动后艺人库会自动重建。",
            FontSize = 12,
            LineBreakMode = LineBreakMode.WordWrap,
            TextColor = ThemeHelper.Color("TextSecondaryColor", "#C2C6E4"),
        });

    private View BuildSeparatorCard(ArtistSplitConfig config)
    {
        var header = ThemeHelper.Label(15, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
        header.Text = "拆分分隔符";

        var inner = new VerticalStackLayout { Spacing = 8, Children = { header } };

        foreach (var builtin in ArtistSplitDefaults.BuiltinSeparators)
        {
            if (config.HiddenBuiltinSeparatorIds.Contains(builtin.Id)) continue;
            var checkedState = config.BuiltinSeparatorOverrides.TryGetValue(builtin.Id, out var v) ? v : builtin.DefaultEnabled;
            inner.Add(BuildToggleRow(builtin.DisplayName ?? builtin.Value, checkedState, on =>
            {
                config.BuiltinSeparatorOverrides[builtin.Id] = on;
                _store.Save(config);
            }));
        }

        _customSepList = new VerticalStackLayout { Spacing = 8 };
        RefreshCustomSeparators(config);

        var addRow = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        _sepInput = new Entry
        {
            Placeholder = "自定义分隔符，如 @ 或 “ ft “",
            TextColor = ThemeHelper.Color("TextPrimaryColor", "#F7F8FF"),
            PlaceholderColor = ThemeHelper.Color("TextSecondaryColor", "#C2C6E4"),
            BackgroundColor = Colors.Transparent,
            FontSize = 13,
        };
        _sepInput.Completed += (_, _) => AddSeparator(config);
        var addBtn = LyricoUi.ActionButton("添加", () => AddSeparator(config));
        addRow.Add(_sepInput, 0);
        addRow.Add(addBtn, 1);

        inner.Add(_customSepList);
        inner.Add(addRow);
        return ThemeHelper.Card(inner);
    }

    private void RefreshCustomSeparators(ArtistSplitConfig config)
    {
        _customSepList!.Children.Clear();
        foreach (var sep in config.CustomSeparators.OrderBy(s => s.Value, StringComparer.Ordinal))
            _customSepList.Children.Add(BuildRemovableRow(sep.Value, sep.Enabled, enabled =>
            {
                sep.Enabled = enabled;
                _store.Save(config);
            }, () =>
            {
                config.CustomSeparators.Remove(sep);
                _store.Save(config);
                RefreshCustomSeparators(config);
            }));
    }

    private void AddSeparator(ArtistSplitConfig config)
    {
        var value = _sepInput?.Text?.Trim();
        if (string.IsNullOrEmpty(value)) return;
        var already = config.CustomSeparators.Any(s => string.Equals(s.Value, value, StringComparison.Ordinal));
        if (!already)
        {
            config.CustomSeparators.Add(new CustomArtistSeparator { Value = value });
            _store.Save(config);
        }
        if (_sepInput != null) _sepInput.Text = "";
        RefreshCustomSeparators(config);
    }

    private View BuildNoSplitCard(ArtistSplitConfig config)
    {
        var header = ThemeHelper.Label(15, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
        header.Text = "不拆分艺人";

        var inner = new VerticalStackLayout { Spacing = 8, Children = { header } };

        foreach (var builtin in ArtistSplitDefaults.BuiltinNoSplitArtists)
        {
            var checkedState = config.BuiltinNoSplitArtistOverrides.TryGetValue(builtin.Id, out var v) ? v : builtin.DefaultEnabled;
            inner.Add(BuildToggleRow(builtin.Name, checkedState, on =>
            {
                config.BuiltinNoSplitArtistOverrides[builtin.Id] = on;
                _store.Save(config);
            }));
        }

        _noSplitList = new VerticalStackLayout { Spacing = 8 };
        RefreshNoSplitArtists(config);

        var addRow = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        _noSplitInput = new Entry
        {
            Placeholder = "整名艺人，如 The Beatles",
            TextColor = ThemeHelper.Color("TextPrimaryColor", "#F7F8FF"),
            PlaceholderColor = ThemeHelper.Color("TextSecondaryColor", "#C2C6E4"),
            BackgroundColor = Colors.Transparent,
            FontSize = 13,
        };
        _noSplitInput.Completed += (_, _) => AddNoSplitArtist(config);
        var addBtn = LyricoUi.ActionButton("添加", () => AddNoSplitArtist(config));
        addRow.Add(_noSplitInput, 0);
        addRow.Add(addBtn, 1);

        inner.Add(_noSplitList);
        inner.Add(addRow);
        return ThemeHelper.Card(inner);
    }

    private void RefreshNoSplitArtists(ArtistSplitConfig config)
    {
        _noSplitList!.Children.Clear();
        foreach (var art in config.CustomNoSplitArtists.OrderBy(a => a.Name, StringComparer.Ordinal))
            _noSplitList.Children.Add(BuildRemovableRow(art.Name, art.Enabled, enabled =>
            {
                art.Enabled = enabled;
                _store.Save(config);
            }, () =>
            {
                config.CustomNoSplitArtists.Remove(art);
                _store.Save(config);
                RefreshNoSplitArtists(config);
            }));
    }

    private void AddNoSplitArtist(ArtistSplitConfig config)
    {
        var name = _noSplitInput?.Text?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        var already = config.CustomNoSplitArtists
            .Any(a => string.Equals(ArtistSplitDefaults.NormalizedKey(a.Name), ArtistSplitDefaults.NormalizedKey(name)));
        if (!already)
        {
            config.CustomNoSplitArtists.Add(new CustomNoSplitArtist { Name = name });
            _store.Save(config);
        }
        if (_noSplitInput != null) _noSplitInput.Text = "";
        RefreshNoSplitArtists(config);
    }

    private View BuildResetButton()
    {
        var reset = new Button
        {
            Text = "恢复默认",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = ThemeHelper.Color("TextSecondaryColor", "#C2C6E4"),
            BackgroundColor = Colors.Transparent,
            CornerRadius = 16,
            HeightRequest = 44,
        };
        reset.Clicked += async (_, _) =>
        {
            var ok = await DisplayAlert("恢复默认", "重置为默认拆分分隔符与不拆分艺人名单。", "恢复默认", "取消");
            if (ok)
            {
                _store.Reset();
                Content = new ScrollView { Content = BuildContent() };
            }
        };
        return reset;
    }

    private static View BuildToggleRow(string label, bool checkedState, Action<bool> onChanged)
    {
        var text = ThemeHelper.Label(13, FontAttributes.None, "TextPrimaryColor", "#F7F8FF", true);
        text.Text = label;

        var sw = new Switch { IsToggled = checkedState, VerticalOptions = LayoutOptions.Center };
        sw.Toggled += (_, e) => onChanged(e.Value);

        var row = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        row.Add(text, 0);
        row.Add(sw, 1);
        return row;
    }

    private static View BuildRemovableRow(string label, bool checkedState, Action<bool> onChanged, Action onRemove)
    {
        var text = ThemeHelper.Label(13, FontAttributes.None, "TextPrimaryColor", "#F7F8FF", true);
        text.Text = label;
        text.VerticalOptions = LayoutOptions.Center;

        var sw = new Switch { IsToggled = checkedState, VerticalOptions = LayoutOptions.Center };
        sw.Toggled += (_, e) => onChanged(e.Value);

        var del = new Button
        {
            Text = "✕",
            FontSize = 13,
            TextColor = ThemeHelper.Color("TextSecondaryColor", "#C2C6E4"),
            BackgroundColor = Colors.Transparent,
            CornerRadius = 12,
            WidthRequest = 30,
            HeightRequest = 30,
            Padding = new Thickness(0),
            VerticalOptions = LayoutOptions.Center,
        };
        del.Clicked += (_, _) => onRemove();

        var row = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        row.Add(del, 0);
        row.Add(text, 1);
        row.Add(sw, 2);
        return row;
    }
}