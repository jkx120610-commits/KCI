using KCI.Core;
using KCI.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KCI;

public partial class MainWindow : Window
{
    private readonly HardwareMonitorEngine _hardware = new();
    private readonly GpuPerformanceService _gpuPerformance = new();
    private readonly WlanService _wlanService = new();
    private SystemInfoService _systemInfo = new();
    private readonly ProcessService _processService = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<string, UserControl> _pages = [];
    private IReadOnlyList<ProcessMetric> _lastProcesses = [];
    private GpuCounterSnapshot _lastGpuCounters = new();
    private bool _refreshing;
    private bool _staticRefreshRunning;
    private int _tickCount;
    private string _currentPage = "CPU";

    public MainWindow()
    {
        InitializeComponent();
        _pages["CPU"] = new CpuPage();
        _pages["GPU"] = new GpuPage();
        _pages["WLAN"] = new WlanPage(_wlanService);
        _pages["Battery"] = new BatteryPage();
        _pages["Display"] = new DisplayPage();
        _pages["Memory"] = new MemoryPage();
        _pages["Help"] = new HelpPage();
        _pages["Info"] = new InfoPage();
        _timer.Tick += Timer_Tick;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!ThemeManager.HasSavedPreference)
            {
                var follow = MessageBox.Show(
                    this,
                    "是否跟随 Windows 系统深浅色模式？\n\n" +
                    "选择“否”将固定使用白色主题。以后可以在系统中切换时不再自动变化。",
                    "KCI 主题设置",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                ThemeManager.SetFollowSystem(follow == MessageBoxResult.Yes);
            }
            _systemInfo.LoadCache();
            ApplyStaticInfo();
            Navigate("CPU");
            LoadingOverlay.Visibility = Visibility.Collapsed;
            _timer.Start();
            _ = InitializeSensorsAsync();
            await RefreshSnapshotAsync();
            if (_systemInfo.ShouldRefreshCache)
            {
                _ = RefreshStaticInfoAsync();
            }
        }
        catch (Exception ex)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            MessageBox.Show(ex.ToString(), "KCI 初始化失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task InitializeSensorsAsync()
    {
        try
        {
            await Task.Run(() => _hardware.Open());
        }
        catch
        {
            // Sensor initialization is reported by HardwareMonitorEngine.
        }
    }

    private void ApplyStaticInfo()
    {
        HostText.Text = string.IsNullOrWhiteSpace(_systemInfo.ComputerSummary)
            ? "正在读取主机信息"
            : $"{_systemInfo.ComputerSummary} · {_systemInfo.OperatingSystem}";
        foreach (var page in _pages.Values)
        {
            switch (page)
            {
                case CpuPage cpu:
                    cpu.SetStaticInfo(_systemInfo);
                    break;
                case GpuPage gpu:
                    gpu.SetStaticInfo(_systemInfo);
                    break;
                case BatteryPage battery:
                    battery.SetStaticInfo(_systemInfo);
                    break;
                case DisplayPage display:
                    display.SetStaticInfo(_systemInfo);
                    break;
                case MemoryPage memory:
                    memory.SetStaticInfo(_systemInfo);
                    break;
                case InfoPage info:
                    info.SetStaticInfo(_systemInfo);
                    break;
            }
        }
    }

    private async Task RefreshStaticInfoAsync()
    {
        if (_staticRefreshRunning)
        {
            return;
        }

        _staticRefreshRunning = true;
        try
        {
            var fresh = await Task.Run(() =>
            {
                var service = new SystemInfoService();
                service.Refresh();
                service.SaveCache();
                return service;
            });
            _systemInfo = fresh;
            ApplyStaticInfo();
        }
        catch
        {
            // Cached data remains available if the background refresh fails.
        }
        finally
        {
            _staticRefreshRunning = false;
        }
    }

    private async void Timer_Tick(object? sender, EventArgs e)
    {
        await RefreshSnapshotAsync();
    }

    private async Task RefreshSnapshotAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            var needsProcesses = _currentPage is "CPU" or "GPU" or "Memory";
            var sampleProcesses = needsProcesses && _tickCount % 3 == 0;
            var refreshBattery = _currentPage == "Battery" && _tickCount % 3 == 0;
            var sampleGpuCounters = _currentPage == "GPU" || _tickCount % 4 == 0;
            _tickCount++;
            var snapshot = await Task.Run(() =>
            {
                var metrics = _hardware.Sample();
                var systemLoad = _processService.GetSystemCpuLoad();
                if (!double.IsNaN(systemLoad))
                {
                    metrics.CpuLoad = systemLoad;
                }

                var gpu = _systemInfo.PrimaryGpu;
                var gpuCounters = sampleGpuCounters &&
                                  gpu is not null &&
                                  (gpu.LuidLow != 0 || gpu.LuidHigh != 0)
                    ? _gpuPerformance.Sample(gpu.LuidLow, gpu.LuidHigh)
                    : _lastGpuCounters;
                if (!double.IsNaN(gpuCounters.Utilization))
                {
                    metrics.GpuLoad = gpuCounters.Utilization;
                }
                metrics.GpuDedicatedUsed = gpuCounters.DedicatedUsed;
                metrics.GpuSharedUsed = gpuCounters.SharedUsed;
                metrics.GpuTotalCommitted = gpuCounters.TotalCommitted;
                if (sampleGpuCounters)
                {
                    _lastGpuCounters = gpuCounters;
                }

                if (sampleProcesses)
                {
                    _lastProcesses = _processService.Sample();
                    if (gpuCounters.ProcessUsage.Count > 0)
                    {
                        foreach (var process in _lastProcesses)
                        {
                            process.GpuPercent = gpuCounters.ProcessUsage.GetValueOrDefault(process.Pid);
                        }
                    }
                }
                if (refreshBattery)
                {
                    _systemInfo.RefreshBatteryDynamic();
                }
                ApplyPowerEstimate(metrics);
                var wlan = _currentPage == "WLAN"
                    ? _wlanService.Sample()
                    : null;
                return new Snapshot
                {
                    Metrics = metrics,
                    Processes = _lastProcesses,
                    ProcessDataChanged = sampleProcesses,
                    Wlan = wlan
                };
            });

            HeaderCpuText.Text = Formatting.Percent(snapshot.Metrics.CpuLoad, 0);
            HeaderGpuText.Text = Formatting.Percent(snapshot.Metrics.GpuLoad, 0);
            HeaderMemoryText.Text = Formatting.Percent(snapshot.Metrics.MemoryLoad, 0);
            HeaderSensorText.Text = snapshot.Metrics.SensorStatus.Length > 22
                ? "已连接"
                : snapshot.Metrics.SensorStatus;

            if (refreshBattery && _pages["Battery"] is BatteryPage batteryPage)
            {
                batteryPage.SetStaticInfo(_systemInfo);
            }

            switch (_pages[_currentPage])
            {
                case CpuPage cpu:
                    cpu.Refresh(snapshot, _systemInfo.CpuSpecs);
                    break;
                case GpuPage gpu:
                    gpu.Refresh(snapshot);
                    break;
                case WlanPage wlan:
                    wlan.Refresh(snapshot.Wlan);
                    break;
                case BatteryPage battery:
                    battery.Refresh(snapshot);
                    break;
                case MemoryPage memory:
                    memory.Refresh(snapshot, _systemInfo);
                    break;
            }
        }
        catch
        {
            HeaderSensorText.Text = "读取异常";
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void ApplyPowerEstimate(HardwareMetrics metrics)
    {
        var cpuLoad = double.IsNaN(metrics.CpuLoad) ? 0 : Math.Clamp(metrics.CpuLoad, 0, 100);
        var gpuLoad = double.IsNaN(metrics.GpuLoad) ? 0 : Math.Clamp(metrics.GpuLoad, 0, 100);

        if (double.IsNaN(metrics.CpuPowerWatts) || metrics.CpuPowerWatts <= 0)
        {
            metrics.CpuPowerWatts = 12d + cpuLoad * 0.45d;
            metrics.CpuPowerEstimated = true;
        }
        if (double.IsNaN(metrics.GpuPowerWatts) || metrics.GpuPowerWatts <= 0)
        {
            metrics.GpuPowerWatts = 8d + gpuLoad * 0.95d;
            metrics.GpuPowerEstimated = true;
        }

        var baselineOther =
            6d +
            _systemInfo.MemoryModules.Count * 2.5d +
            _systemInfo.StorageDevices.Count * 3d +
            Math.Max(1, _systemInfo.Displays.Count) * 4d +
            (_systemInfo.Battery.Present ? 1d : 0d);
        if (double.IsNaN(metrics.OtherPowerWatts) || metrics.OtherPowerWatts <= 0)
        {
            metrics.OtherPowerWatts = baselineOther;
            metrics.OtherPowerEstimated = true;
        }
        else
        {
            metrics.OtherPowerWatts = Math.Max(metrics.OtherPowerWatts, baselineOther);
        }

        metrics.EstimatedSupplyWatts =
            metrics.CpuPowerWatts + metrics.GpuPowerWatts + metrics.OtherPowerWatts;
        metrics.PowerEstimateNote =
            $"CPU {(metrics.CpuPowerEstimated ? "估算" : "传感器")} · " +
            $"GPU {(metrics.GpuPowerEstimated ? "估算" : "传感器")} · " +
            $"其他设备 {(metrics.OtherPowerEstimated ? "估算" : "传感器")}";
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page })
        {
            Navigate(page);
        }
    }

    private void Navigate(string page)
    {
        if (!_pages.TryGetValue(page, out var control))
        {
            return;
        }

        _currentPage = page;
        PageHost.Content = control;
        PageHost.Opacity = 0;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        Timeline.SetDesiredFrameRate(fade, 60);
        PageHost.BeginAnimation(OpacityProperty, fade);
        PageTitleText.Text = page switch
        {
            "Battery" => "电池",
            "WLAN" => "WLAN",
            "Display" => "屏幕",
            "Memory" => "内存与存储",
            "Help" => "KCI Help",
            "Info" => "KCI 信息",
            _ => page
        };

        foreach (var button in new[]
                 {
                     CpuNavButton, GpuNavButton, WlanNavButton, BatteryNavButton, DisplayNavButton,
                     MemoryNavButton, HelpNavButton, InfoNavButton
                 })
        {
            button.Style = (Style)FindResource(
                Equals(button.Tag, page) ? "NavButtonActive" : "NavButton");
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaximizeButton_Click(sender, e);
            return;
        }
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _gpuPerformance.Dispose();
        _hardware.Dispose();
    }
}
