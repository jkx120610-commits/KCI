using KCI.Controls;
using KCI.Core;
using System.Windows;
using System.Windows.Controls;

namespace KCI.Views;

public partial class DisplayPage : UserControl
{
    private SystemInfoService? _info;

    public DisplayPage()
    {
        InitializeComponent();
    }

    public void SetStaticInfo(SystemInfoService info)
    {
        _info = info;
        DisplayItems.ItemsSource = info.Displays;
        AdapterItems.ItemsSource = info.Gpus;
    }

    private void ColorTestButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new DisplayTestWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_info is null)
        {
            return;
        }
        _info.Refresh();
        SetStaticInfo(_info);
    }
}
