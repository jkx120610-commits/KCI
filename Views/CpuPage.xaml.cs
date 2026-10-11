using KCI.Core;
using System.Windows;
using System.Windows.Controls;

namespace KCI.Views;

public partial class CpuPage : UserControl
{
    public CpuPage()
    {
        InitializeComponent();
    }

    public void SetStaticInfo(SystemInfoService info)
    {
        CpuNameText.Text = info.CpuSpecs.FirstOrDefault(item => item.Label == "型号")?.Value ?? "未知处理器";
        SpecsItems.ItemsSource = info.CpuSpecs;
    }

    public void Refresh(Snapshot snapshot, IReadOnlyList<SpecItem> specs)
    {
        var metric = snapshot.Metrics;
        LoadRing.Value = metric.CpuLoad;
        TempRing.Value = metric.CpuTemp;
        LoadChart.Push(metric.CpuLoad);
        CpuStatusText.Text = metric.SensorStatus;

        if (snapshot.ProcessDataChanged)
        {
            var selectedPid = (ProcessGrid.SelectedItem as ProcessMetric)?.Pid;
            ProcessGrid.ItemsSource = snapshot.Processes
                .OrderByDescending(process => process.CpuPercent)
                .ThenByDescending(process => process.MemoryMb)
                .Take(24)
                .ToList();
            if (selectedPid.HasValue)
            {
                ProcessGrid.SelectedItem = (ProcessGrid.ItemsSource as IEnumerable<ProcessMetric>)?
                    .FirstOrDefault(process => process.Pid == selectedPid.Value);
            }
        }
    }

    private void CloseProcessButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProcessGrid.SelectedItem is not ProcessMetric process)
        {
            MessageBox.Show("请先选择要结束的应用。", "KCI");
            return;
        }
        if (process.IsProtected || process.Pid == Environment.ProcessId)
        {
            MessageBox.Show("该系统进程受保护，不能由 KCI 结束。", "KCI",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (MessageBox.Show($"确定结束 {process.Name} ({process.Pid})？",
                "KCI", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            using var target = System.Diagnostics.Process.GetProcessById(process.Pid);
            target.CloseMainWindow();
            if (!target.WaitForExit(1200))
            {
                target.Kill(true);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "结束应用失败");
        }
    }
}
