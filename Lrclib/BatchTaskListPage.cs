using System.ComponentModel;
using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 批量任务中心：列出已执行的批量操作历史（对齐 Lyrico BatchTaskListScreen）。
/// 每项显示 模式 · 成功/总数，点击查看逐首明细；支持清空历史。
/// </summary>
public class BatchTaskListPage : ContentPage
{
    private readonly ObservableCollection<BatchTaskRecord> _records = new();
    private readonly CollectionView _list;

    public BatchTaskListPage()
    {
        Title = "批量任务历史";
        BackgroundColor = ThemeHelper.Color("WindowBackgroundColor", "#1A1838");

        var clearButton = new Button
        {
            Text = "清空历史",
            FontSize = 13,
            Padding = new Thickness(14, 5),
            BackgroundColor = ThemeHelper.Color("PrimaryColor", "#8C7BFF"),
            TextColor = Colors.White,
            CornerRadius = 14,
        };
        clearButton.Clicked += ClearHistory;

        var header = new Grid
        {
            Margin = new Thickness(12, 8, 12, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        var title = ThemeHelper.Label(14, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
        title.Text = "最近执行的批量操作";
        header.Add(title, 0);
        header.Add(clearButton, 1);

        _list = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            Margin = new Thickness(8, 8, 8, 0),
            ItemTemplate = new DataTemplate(BuildRow),
        };
        _list.ItemsSource = _records;
        _list.SelectionChanged += OnSelected;

        var empty = ThemeHelper.Label(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        empty.Text = "暂无批量任务历史";
        empty.HorizontalOptions = LayoutOptions.Center;
        empty.Margin = new Thickness(0, 20, 0, 0);
        _emptyLabel = empty;

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
        };
        root.Add(header, 0, 0);
        root.Add(_list, 0, 1);
        root.Add(empty, 0, 1);   // 与列表同行，互斥显示
        Content = root;
    }

    private readonly Label _emptyLabel;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Reload();
    }

    private void Reload()
    {
        _records.Clear();
        foreach (var r in BatchTaskStore.GetAll()) _records.Add(r);
        _emptyLabel.IsVisible = _records.Count == 0;
    }

    private async void ClearHistory(object? sender, EventArgs e)
    {
        // 页面实例方法 DisplayAlert（桌面宿主无 Shell，Shell.Current 为 null）
        var ok = await DisplayAlert("清空历史", "确定删除全部批量任务历史记录？", "清空", "取消");
        if (!ok) return;
        await Task.Run(() => BatchTaskStore.Clear());
        Reload();
    }

    private View BuildRow()
    {
        var title = ThemeHelper.Label(14, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
        title.SetBinding(Label.TextProperty, nameof(BatchTaskRecord.Mode));

        var summary = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        summary.SetBinding(Label.TextProperty, nameof(BatchTaskRecord.Summary));

        var time = ThemeHelper.Label(11, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", false);
        time.SetBinding(Label.TextProperty, nameof(BatchTaskRecord.TimeText));
        time.VerticalOptions = LayoutOptions.Center;

        var dot = new Border
        {
            HeightRequest = 10,
            WidthRequest = 10,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(5) },
            VerticalOptions = LayoutOptions.Center,
        };
        dot.SetBinding(VisualElement.BackgroundColorProperty,
            new Binding(nameof(BatchTaskRecord.AllSuccess))
            {
                Converter = new BoolToColorConverter(Color.FromArgb("#4ADE80"), Color.FromArgb("#F87171")),
            });

        var text = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Spacing = 2,
            Children = { title, summary },
        };

        var row = new Grid
        {
            Padding = new Thickness(8, 6),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        row.Add(dot, 0);
        row.Add(text, 1);
        row.Add(time, 2);

        return LyricoUi.Card(row);
    }

    private async void OnSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not BatchTaskRecord record) return;
        _list.SelectedItem = null;
        await PluginNav.PushAsync(new BatchTaskDetailPage(record));
    }
}