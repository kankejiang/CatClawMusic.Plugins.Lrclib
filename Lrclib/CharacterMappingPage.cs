using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 字符映射配置页（Lyrico <c>CharacterMappingScreen</c> 复刻）：
/// 管理批量重命名时的字符映射规则（from→to），保存后 <see cref="BatchOperationsViewModel.SanitizeFileName"/> 复用。
/// </summary>
public class CharacterMappingPage : ContentPage
{
    private readonly CharacterMappingStore _store = new();
    private readonly ObservableCollection<MappingItem> _items = new();
    private readonly Entry _fromEntry = new();
    private readonly Entry _toEntry = new();
    private readonly Label _status = new();

    public CharacterMappingPage()
    {
        Title = "字符映射";
        BackgroundColor = ThemeHelper.Color("WindowBackgroundColor", "#1A1838");

        foreach (var (from, to) in _store.GetMappings()) _items.Add(new MappingItem(from, to));

        var hint = new Label
        {
            Text = "批量重命名时，文件名中的「源字符」会替换为「目标字符」。默认把非法符（\\ / : * ? \" < > |）映射为全角等价符，避免丢失分隔字符。",
            FontSize = 11,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        hint.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");

        _fromEntry.Placeholder = "源字符";
        _fromEntry.MaxLength = 1;
        _toEntry.Placeholder = "目标字符";
        _toEntry.MaxLength = 1;
        BindEntry(_fromEntry);
        BindEntry(_toEntry);

        var addButton = new Button { Text = "添加", FontSize = 13, Padding = new Thickness(12, 4) };
        addButton.SetDynamicResource(Button.TextColorProperty, "PrimaryColor");
        addButton.Clicked += (_, _) => AddMapping();

        _fromEntry.Completed += (_, _) => AddMapping();
        _toEntry.Completed += (_, _) => AddMapping();

        var addRow = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };
        addRow.Add(_fromEntry, 0);
        addRow.Add(_toEntry, 1);
        addRow.Add(addButton, 2);

        var listHeader = new Label { Text = "映射规则", FontSize = 13, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 8, 0, 0) };
        listHeader.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");

        var list = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(() =>
            {
                var fromLabel = new Label { FontSize = 16, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center };
                fromLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");
                fromLabel.SetBinding(Label.TextProperty, nameof(MappingItem.FromText));
                var arrow = new Label { Text = "→", FontSize = 14, VerticalOptions = LayoutOptions.Center };
                arrow.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
                var toLabel = new Label { FontSize = 16, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center };
                toLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");
                toLabel.SetBinding(Label.TextProperty, nameof(MappingItem.ToText));
                var del = new Button { Text = "删除", FontSize = 12, Padding = new Thickness(10, 2) };
                del.SetDynamicResource(Button.TextColorProperty, "PrimaryColor");
                del.Clicked += (_, _) =>
                {
                    if (del.BindingContext is MappingItem item) _items.Remove(item);
                };
                var row = new Grid
                {
                    Padding = new Thickness(4, 6),
                    ColumnSpacing = 10,
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Auto),
                        new ColumnDefinition(GridLength.Auto),
                        new ColumnDefinition(GridLength.Auto),
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Auto),
                    },
                };
                row.Add(fromLabel, 0);
                row.Add(arrow, 1);
                row.Add(toLabel, 2);
                row.Add(del, 4);
                return row;
            }),
        };
        list.ItemsSource = _items;

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

        var resetButton = new Button { Text = "恢复默认", FontSize = 13, Padding = new Thickness(16, 8), Margin = new Thickness(0, 4, 0, 0) };
        resetButton.SetDynamicResource(Button.TextColorProperty, "PrimaryColor");
        resetButton.Clicked += (_, _) =>
        {
            _items.Clear();
            foreach (var (from, to) in CharacterMappingStore.DefaultMappings) _items.Add(new MappingItem(from, to));
            _status.Text = "已恢复默认（需点保存生效）";
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(16, 12),
                Spacing = 8,
                Children = { hint, addRow, listHeader, list, _status, saveButton, resetButton },
            }
        };
        WideAdapt.Attach(this, WideAdapt.FormMaxWidth);
    }

    private void AddMapping()
    {
        var from = _fromEntry.Text;
        var to = _toEntry.Text;
        if (string.IsNullOrEmpty(from)) { _status.Text = "请输入源字符"; return; }
        var f = from[0];
        var t = (to ?? "").Length > 0 ? to[0] : ' ';
        // 同源字符已存在则更新
        var existing = _items.FirstOrDefault(x => x.From == f);
        if (existing != null) { existing.To = t; existing.NotifyChanged(); }
        else _items.Add(new MappingItem(f, t));
        _fromEntry.Text = "";
        _toEntry.Text = "";
    }

    private void Save()
    {
        _store.Save(_items.Select(x => (x.From, x.To)));
        _status.Text = $"已保存 {_items.Count} 条映射";
    }

    private static void BindEntry(Entry e)
    {
        e.FontSize = 14;
        e.WidthRequest = 60;
        e.HorizontalOptions = LayoutOptions.Start;
        e.SetDynamicResource(Entry.TextColorProperty, "TextPrimaryColor");
        e.SetDynamicResource(Entry.PlaceholderColorProperty, "TextSecondaryColor");
    }
}

/// <summary>映射规则展示项。</summary>
public partial class MappingItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public char From { get; set; }
    public char To { get; set; }
    public string FromText => From == '\0' ? "" : From.ToString();
    public string ToText => To == '\0' ? "（删除）" : To.ToString();

    public MappingItem(char from, char to) { From = from; To = to; }

    /// <summary>触发 FromText/ToText 重新读取（编辑现有项时）。</summary>
    public void NotifyChanged()
    {
        OnPropertyChanged(nameof(FromText));
        OnPropertyChanged(nameof(ToText));
    }
}
