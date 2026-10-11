using KCI.Core;
using System.Windows;
using System.Windows.Controls;

namespace KCI.Views;

public partial class MemoryPage : UserControl
{
    private bool _appUsageLoaded;
    private bool _appUsageLoading;

    public MemoryPage()
    {
        InitializeComponent();
    }

    public void SetStaticInfo(SystemInfoService info)
    {
        MemoryGrid.ItemsSource = info.MemoryModules;
        VolumeGrid.ItemsSource = info.Volumes;
        StorageItems.ItemsSource = info.StorageDevices;
        var systemVolume = info.Volumes.FirstOrDefault(volume =>
                               volume.Drive.Equals("C:", StringComparison.OrdinalIgnoreCase))
                           ?? info.Volumes.FirstOrDefault();
        StorageRing.Value = systemVolume?.UsagePercent ?? double.NaN;
        VolumeSummaryText.Text = systemVolume is null
            ? "磁盘空间不可用"
            : $"{systemVolume.Drive} 已使用 {systemVolume.Used} / {systemVolume.Total}";
        VolumeSummaryPercentText.Text = systemVolume is null
            ? string.Empty
            : $"可用 {systemVolume.Free} · {systemVolume.UsagePercent:0.0}% 已使用";
    }

    public void Refresh(Snapshot snapshot, SystemInfoService info)
    {
        MemoryRing.Value = snapshot.Metrics.MemoryLoad;
        MemoryTempRing.Value = snapshot.Metrics.MemoryTemp;
        MemoryChart.Push(snapshot.Metrics.MemoryLoad);

        if (snapshot.ProcessDataChanged)
        {
            var selectedPid = (ProcessGrid.SelectedItem as ProcessMetric)?.Pid;
            ProcessGrid.ItemsSource = snapshot.Processes
                .OrderByDescending(process => process.MemoryMb)
                .ThenByDescending(process => process.CpuPercent)
                .Take(24)
                .ToList();
            if (selectedPid.HasValue)
            {
                ProcessGrid.SelectedItem = (ProcessGrid.ItemsSource as IEnumerable<ProcessMetric>)?
                    .FirstOrDefault(process => process.Pid == selectedPid.Value);
            }
        }

        foreach (var storage in info.StorageDevices)
        {
            var key = snapshot.Metrics.StorageTemperature.Keys.FirstOrDefault(name =>
                name.Contains(storage.Name, StringComparison.OrdinalIgnoreCase) ||
                storage.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (key is not null)
            {
                storage.Temperature = snapshot.Metrics.StorageTemperature[key];
            }

            var usageKey = snapshot.Metrics.StorageUsage.Keys.FirstOrDefault(name =>
                name.Contains(storage.Name, StringComparison.OrdinalIgnoreCase) ||
                storage.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (usageKey is not null)
            {
                storage.UsagePercent = snapshot.Metrics.StorageUsage[usageKey];
            }
        }
        StorageItems.Items.Refresh();
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

    private async void RefreshAppUsageButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAppUsageAsync(force: true);
    }

    private async void StorageTabs_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (StorageTabs.SelectedIndex == 1)
        {
            await RefreshAppUsageAsync();
        }
    }

    private async Task RefreshAppUsageAsync(bool force = false)
    {
        if (_appUsageLoading || (_appUsageLoaded && !force))
        {
            return;
        }

        _appUsageLoading = true;
        RefreshAppUsageButton.IsEnabled = false;
        AppUsageStatusText.Text = "正在扫描所有固定磁盘上的应用、图标与占用空间...";
        try
        {
            if (force)
            {
                InstalledAppService.ClearDirectorySizeCache();
            }

            var apps = await Task.Run(() => InstalledAppService.GetInstalledApps());
            var rows = apps
                .Where(app => !app.IsSystemApp &&
                              !app.Name.Equals("KCI", StringComparison.OrdinalIgnoreCase) &&
                              !app.Publisher.Contains("Kerwin", StringComparison.OrdinalIgnoreCase))
                .Select(app => new
                {
                    App = app,
                    Size = InstalledAppService.GetMeasuredSize(app)
                })
                .ToList();

            var knownTotal = rows.Sum(item => Math.Max(0, item.Size));
            AppUsageGrid.ItemsSource = rows
                .OrderByDescending(item => item.Size)
                .ThenBy(item => item.App.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(item => new AppUsageInfo
                {
                    Icon = item.App.Icon,
                    Name = item.App.Name,
                    Publisher = item.App.Publisher,
                    Location = string.IsNullOrWhiteSpace(item.App.InstallLocation)
                        ? "系统未报告"
                        : item.App.InstallLocation,
                    SizeBytes = item.Size,
                    SharePercent = knownTotal > 0 && item.Size > 0
                        ? item.Size / (double)knownTotal * 100d
                        : 0,
                    SizeSource = item.App.SizeSource
                })
                .ToList();

            AppUsageStatusText.Text =
                $"应用自身安装占用 {Formatting.Bytes(knownTotal)}，共 {rows.Count} 个桌面应用；" +
                "该数值不包含 Windows、文档、媒体和其他非应用数据，" +
                "应用占比按所有已识别应用的总安装体积计算。";
            _appUsageLoaded = true;
        }
        catch (Exception ex)
        {
            AppUsageStatusText.Text = $"应用占用扫描失败：{ex.Message}";
        }
        finally
        {
            _appUsageLoading = false;
            RefreshAppUsageButton.IsEnabled = true;
        }
    }
}
