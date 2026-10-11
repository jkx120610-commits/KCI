using KCI.Core;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace KCI.Controls;

public partial class AppDialog : Window
{
    private readonly List<InstalledAppInfo> _source;
    private readonly ObservableCollection<InstalledAppInfo> _visible = [];
    private readonly bool _showSelection;

    public IReadOnlyList<InstalledAppInfo> SelectedItems =>
        _source.Where(item => item.IsSelected).ToList();

    public AppDialog(
        string title,
        string subtitle,
        string warning,
        IEnumerable<InstalledAppInfo> apps,
        bool showSelection = true,
        string confirmText = "继续")
    {
        InitializeComponent();
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
        WarningText.Text = warning;
        ConfirmButton.Content = confirmText;
        _source = apps.ToList();
        _showSelection = showSelection;
        SelectionColumn.Visibility = showSelection ? Visibility.Visible : Visibility.Collapsed;
        SelectSafeButton.Visibility = showSelection ? Visibility.Visible : Visibility.Collapsed;
        ItemsGrid.ItemsSource = _visible;
        ApplyFilter();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var term = SearchBox.Text.Trim();
        _visible.Clear();
        foreach (var app in _source.Where(app =>
                     string.IsNullOrWhiteSpace(term) ||
                     app.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                     app.Publisher.Contains(term, StringComparison.CurrentCultureIgnoreCase)))
        {
            _visible.Add(app);
        }
        UpdateCount();
    }

    private void SelectSafeButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var app in _visible)
        {
            app.IsSelected = !app.IsSystemApp;
        }
        ItemsGrid.Items.Refresh();
        UpdateCount();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (_showSelection && !_source.Any(item => item.IsSelected))
        {
            MessageBox.Show(this, "请至少选择一项。", "KCI", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
        Close();
    }

    private void UpdateCount()
    {
        CountText.Text = _showSelection
            ? $"显示 {_visible.Count} 项，已选择 {_source.Count(item => item.IsSelected)} 项"
            : $"显示 {_visible.Count} 项";
    }
}
