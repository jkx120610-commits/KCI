using KCI.Core;
using System.Collections.ObjectModel;
using System.Windows;

namespace KCI.Controls;

public partial class ProcessDialog : Window
{
    private readonly ObservableCollection<ProcessChoice> _items;

    public IReadOnlyList<ProcessMetric> SelectedProcesses =>
        _items.Where(item => item.IsSelected)
            .Select(item => item.Process)
            .ToList();

    public ProcessDialog(IEnumerable<ProcessMetric> processes)
    {
        InitializeComponent();
        _items = new ObservableCollection<ProcessChoice>(
            processes.Select(process => new ProcessChoice(process)));
        ItemsGrid.ItemsSource = _items;
        foreach (var item in _items)
        {
            item.PropertyChanged += (_, _) => UpdateCount();
        }
        UpdateCount();
    }

    private void UpdateCount()
    {
        CountText.Text = $"共 {_items.Count} 个可结束应用，已选择 " +
                         $"{_items.Count(item => item.IsSelected)} 个，预计释放 " +
                         $"{_items.Where(item => item.IsSelected).Sum(item => item.Process.MemoryMb):0.0} MB";
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
            MessageBox.Show(this, "请至少选择一个应用。", "KCI", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
        Close();
    }
}

public sealed class ProcessChoice : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isSelected = true;

    public ProcessMetric Process { get; }
    public int Pid => Process.Pid;
    public string Name => Process.Name;
    public string Description => Process.Description;
    public string MemoryText => $"{Process.MemoryMb:0.0} MB";
    public string CpuText => $"{Process.CpuPercent:0.0}%";
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }
            _isSelected = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public ProcessChoice(ProcessMetric process) => Process = process;
}
