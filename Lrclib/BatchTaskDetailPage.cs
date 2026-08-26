using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 批量任务详情页：展示一次批量操作的总体结果与逐首明细（对齐 Lyrico BatchTaskDetailScreen）。
/// </summary>
public class BatchTaskDetailPage : ContentPage
{
    private readonly BatchTaskRecord _record;

    public BatchTaskDetailPage(BatchTaskRecord record)
    {
        _record = record;
        Title = record.Mode;
        BackgroundColor = ThemeHelper.Color("WindowBackgroundColor", "#1A1838");

        var summary = LyricoUi.Card(new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                Row("模式", record.Mode),
                Row("执行时间", record.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")),
                Row("结果", $"{record.SuccessCount} 成功 · {record.FailCount} 失败 / 共 {record.Total}"),
            },
        });

        var items = new ObservableCollection<BatchTaskItemRecord>(record.Items);
        var list = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            Margin = new Thickness(8, 4, 8, 0),
            ItemTemplate = new DataTemplate(BuildItemRow),
        };
        list.ItemsSource = items;

        var itemsHeader = ThemeHelper.Label(13, FontAttributes.Bold, "TextSecondaryColor", "#C2C6E4", true);
        itemsHeader.Text = $"逐首明细（{items.Count}）";
        itemsHeader.Margin = new Thickness(16, 10, 16, 0);

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
        };
        root.Add(summary, 0, 0);
        root.Add(itemsHeader, 0, 1);
        root.Add(list, 0, 2);
        Content = root;
    }

    private static View Row(string label, string value)
    {
        var l = ThemeHelper.Label(13, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        l.Text = label;
        var v = ThemeHelper.Label(13, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
        v.Text = value;

        var g = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };
        g.Add(l, 0, 0);
        g.Add(v, 1, 0);
        return g;
    }

    private View BuildItemRow()
    {
        var title = ThemeHelper.Label(14, FontAttributes.Bold, "TextPrimaryColor", "#F7F8FF", true);
        title.SetBinding(Label.TextProperty, nameof(BatchTaskItemRecord.Title));

        var status = ThemeHelper.Label(12, FontAttributes.None, "TextSecondaryColor", "#C2C6E4", true);
        status.SetBinding(Label.TextProperty, nameof(BatchTaskItemRecord.Status));
        status.SetBinding(Label.TextColorProperty, new Binding(nameof(BatchTaskItemRecord.Success))
        {
            Converter = new BoolToColorConverter(Color.FromArgb("#4ADE80"), Color.FromArgb("#F87171")),
        });

        var text = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            Spacing = 2,
            Children = { title, status },
        };

        return new Grid
        {
            Padding = new Thickness(8, 5),
            Children = { text },
        };
    }
}