using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 歌词清理规则页（Lyrico <c>LyricsCleanupRulesScreen</c> 复刻）：
/// 管理标签行过滤关键词（增删）+ 去空行默认开关，保存后供 SearchLyrics/BatchLyricsFormat 复用。
/// 纯 C# 构建，复用宿主全局主题资源。
/// </summary>
public class LyricCleanupRulesPage : ContentPage
{
    private readonly LyricCleanupRulesStore _store = new();
    private readonly ObservableCollection<string> _keywords = new();
    private readonly Entry _addEntry = new();
    private readonly Switch _emptySwitch = new();
    private readonly Label _status = new();

    public LyricCleanupRulesPage()
    {
        Title = "歌词清理规则";
        BackgroundColor = ThemeHelper.Color("WindowBackgroundColor", "#1A1838");

        foreach (var k in _store.GetTagKeywords()) _keywords.Add(k);
        _emptySwitch.IsToggled = _store.GetRemoveEmptyLinesDefault();
        _emptySwitch.OnColor = ThemeHelper.Color("PrimaryColor", "#8C7BFF");

        _addEntry.Placeholder = "新增关键词（如 [custom:）";
        _addEntry.SetDynamicResource(Entry.TextColorProperty, "TextPrimaryColor");
        _addEntry.SetDynamicResource(Entry.PlaceholderColorProperty, "TextSecondaryColor");
        _addEntry.ReturnType = ReturnType.Done;
        _addEntry.Completed += (_, _) => AddKeyword();

        var addButton = new Button { Text = "添加", FontSize = 13, Padding = new Thickness(12, 4) };
        addButton.SetDynamicResource(Button.TextColorProperty, "PrimaryColor");
        addButton.Clicked += (_, _) => AddKeyword();

        var addRow = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        addRow.Add(_addEntry, 0);
        addRow.Add(addButton, 1);

        var hint = new Label
        {
            Text = "含这些关键词的行会被过滤（LRC 元数据标签行如 [ti:歌名）。去空行控制是否过滤空白/占位行。",
            FontSize = 11,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        hint.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");

        var kwHeader = new Label { Text = "过滤关键词", FontSize = 13, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 8, 0, 0) };
        kwHeader.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");

        var kwList = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(() =>
            {
                var kwLabel = new Label { FontSize = 14, VerticalOptions = LayoutOptions.Center };
                kwLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");
                kwLabel.SetBinding(Label.TextProperty, ".");
                var del = new Button { Text = "删除", FontSize = 12, Padding = new Thickness(10, 2) };
                del.SetDynamicResource(Button.TextColorProperty, "PrimaryColor");
                del.Clicked += (_, _) =>
                {
                    if (del.BindingContext is string kw) _keywords.Remove(kw);
                };
                var row = new Grid
                {
                    Padding = new Thickness(4, 4),
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                };
                row.Add(kwLabel, 0);
                row.Add(del, 1);
                return row;
            }),
        };
        kwList.ItemsSource = _keywords;

        var emptyRow = new Grid
        {
            Margin = new Thickness(0, 8, 0, 0),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        var emptyLabel = new Label
        {
            Text = "去空行（默认）",
            FontSize = 14,
            VerticalOptions = LayoutOptions.Center,
        };
        emptyLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");
        emptyRow.Add(emptyLabel, 0);
        emptyRow.Add(_emptySwitch, 1);

        _status.FontSize = 12;
        _status.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");

        var saveButton = new Button
        {
            Text = "保存",
            FontSize = 14,
            TextColor = Colors.White,
            BackgroundColor = ThemeHelper.Color("PrimaryColor", "#8C7BFF"),
            CornerRadius = 14,
            Padding = new Thickness(16, 8),
            Margin = new Thickness(0, 12, 0, 0),
        };
        saveButton.Clicked += (_, _) => Save();

        var resetButton = new Button
        {
            Text = "恢复默认",
            FontSize = 13,
            Padding = new Thickness(16, 8),
            Margin = new Thickness(0, 4, 0, 0),
        };
        resetButton.SetDynamicResource(Button.TextColorProperty, "PrimaryColor");
        resetButton.Clicked += (_, _) =>
        {
            _keywords.Clear();
            foreach (var k in LyricCleanupRulesStore.DefaultTagKeywords) _keywords.Add(k);
            _emptySwitch.IsToggled = true;
            _status.Text = "已恢复默认（需点保存生效）";
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(16, 12),
                Spacing = 8,
                Children = { hint, addRow, kwHeader, kwList, emptyRow, _status, saveButton, resetButton },
            }
        };
        WideAdapt.Attach(this, WideAdapt.FormMaxWidth);
    }

    private void AddKeyword()
    {
        var kw = _addEntry.Text?.Trim();
        if (string.IsNullOrEmpty(kw)) return;
        if (!_keywords.Contains(kw, StringComparer.OrdinalIgnoreCase)) _keywords.Add(kw);
        _addEntry.Text = "";
    }

    private void Save()
    {
        _store.Save(_keywords, _emptySwitch.IsToggled);
        _status.Text = $"已保存（{_keywords.Count} 个关键词，去空行={(_emptySwitch.IsToggled ? "开" : "关")}）";
    }
}
