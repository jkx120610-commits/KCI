using KCI.Core;
using System.Windows;
using System.Windows.Controls;

namespace KCI.Views;

public partial class GpuPage : UserControl
{
    private GpuInfo? _primaryGpu;

    public GpuPage()
    {
        InitializeComponent();
    }

    public void SetStaticInfo(SystemInfoService info)
    {
        GpuItems.ItemsSource = info.Gpus;
        GpuNameText.Text = info.Gpus.Count > 0
            ? string.Join(" · ", info.Gpus.Select(gpu => gpu.Name))
            : "未检测到显卡";
        var primary = info.PrimaryGpu;
        _primaryGpu = primary;
        GpuMemoryText.Text = primary is null
            ? "显存信息不可用"
            : $"{primary.TotalRam} · {primary.AdapterRam} · {primary.SharedRam}";
    }

    public void Refresh(Snapshot snapshot)
    {
        LoadRing.Value = snapshot.Metrics.GpuLoad;
        TempRing.Value = snapshot.Metrics.GpuTemp;
        LoadChart.Push(snapshot.Metrics.GpuLoad);
        SensorStatusText.Text = snapshot.Metrics.SensorStatus;
        GpuMemoryText.Text = !double.IsNaN(snapshot.Metrics.GpuDedicatedUsed)
            ? $"当前显存占用 {Formatting.Bytes((long)snapshot.Metrics.GpuDedicatedUsed)} / " +
              $"{(_primaryGpu?.AdapterRam ?? "专用显存")}"
            : _primaryGpu is null
                ? "显存信息不可用"
                : $"{_primaryGpu.TotalRam} · {_primaryGpu.AdapterRam} · {_primaryGpu.SharedRam}";

        if (snapshot.ProcessDataChanged)
        {
            var selectedPid = (ProcessGrid.SelectedItem as ProcessMetric)?.Pid;
            ProcessGrid.ItemsSource = snapshot.Processes
                .OrderByDescending(process => process.GpuPercent)
                .ThenByDescending(process => process.MemoryMb)
                .ThenByDescending(process => process.CpuPercent)
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
            MessageBox.Show("该系统进程受保护。", "KCI", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        try
        {
            using var target = System.Diagnostics.Process.GetProcessById(process.Pid);
            target.Kill(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "结束应用失败");
        }
    }
}
