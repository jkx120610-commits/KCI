using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace KCI.Controls;

public partial class DisplayTestWindow : Window
{
    private static readonly Color[] TestColors =
    [
        Colors.Red, Colors.Lime, Colors.Blue, Colors.White, Colors.Black,
        Color.FromRgb(32, 32, 32), Color.FromRgb(64, 64, 64), Color.FromRgb(96, 96, 96),
        Color.FromRgb(128, 128, 128), Color.FromRgb(160, 160, 160)
    ];

    private int _index;

    public DisplayTestWindow()
    {
        InitializeComponent();
        ApplyColor();
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _index = (_index + 1) % TestColors.Length;
        ApplyColor();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
        else if (e.Key is Key.Space or Key.Right or Key.Enter)
        {
            _index = (_index + 1) % TestColors.Length;
            ApplyColor();
        }
        else if (e.Key is Key.Left)
        {
            _index = (_index - 1 + TestColors.Length) % TestColors.Length;
            ApplyColor();
        }
    }

    private void ApplyColor()
    {
        Background = new SolidColorBrush(TestColors[_index]);
        HintText.Visibility = _index is 0 or 3 ? Visibility.Visible : Visibility.Collapsed;
    }
}
