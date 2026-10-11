using KCI.Core;
using System.Collections.ObjectModel;
using System.Windows;

namespace KCI.Controls;

public partial class JunkCleanDialog : Window
{
    private readonly ObservableCollection<JunkItem> _items;

    public IReadOnlyList<JunkItem> SelectedItems =>
        _items.Where(item => item.IsSelected).ToList();

    public JunkCleanDialog(IEnumerable<JunkItem> items)
    {
        InitializeComponent();
        _items = new ObservableCollection<JunkItem>(items);
        ItemsGrid.ItemsSource = _items;
        foreach (var item in _items)
        {
            item.IsSelected = item.IsRecommended;
            item.PropertyChanged += (_, _) => UpdateCount();
        }
        UpdateCount();
    }

    private void UpdateCount()
    {
        var selected = _items.Where(item => item.IsSelected).ToList();
        CountText.Text =
            $"共 {_items.Count} 项，选择 {selected.Count} 项，" +
            $"预计清理 {Formatting.Bytes(selected.Sum(item => Math.Max(0, item.SizeBytes)))}";
    }

    private void SelectRecommended_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items)
        {
            item.IsSelected = item.IsRecommended;
        }
        ItemsGrid.Items.Refresh();
        UpdateCount();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!_items.Any(item => item.IsSelected))
        {
            MessageBox.Show(this, "请至少选择一项。", "KCI",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
        Close();
    }
}
