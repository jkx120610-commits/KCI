using KCI.Core;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace KCI.Views;

public partial class WlanPage : UserControl
{
    private readonly WlanService _wlanService;
    private readonly SpeedTestService _speedTestService = new();
    private readonly LanScannerService _lanScannerService = new();
    private readonly NetworkOptimizationService _optimizationService = new();

    public WlanPage(WlanService wlanService)
    {
        _wlanService = wlanService;
        InitializeComponent();
    }

    public void Refresh(WlanSnapshot? wlan)
    {
        if (wlan is null)
        {
            return;
        }

        WlanNameText.Text = wlan.Connected
            ? $"{wlan.Ssid} · {wlan.AdapterName}"
            : "当前没有可用的无线连接";
        StatusBadgeText.Text = wlan.Connected ? "已连接" : "未连接";
        StatusBadgeText.Foreground = wlan.Connected
            ? (System.Windows.Media.Brush)TryFindResource("AccentBrush")
            : (System.Windows.Media.Brush)TryFindResource("WarmBrush");
        SignalRing.Value = wlan.SignalQuality >= 0 ? wlan.SignalQuality : double.NaN;
        SecurityText.Text = $"安全等级：{wlan.SecurityLevel}";
        SecurityAdviceText.Text =
            $"认证：{wlan.Authentication} · 加密：{wlan.Cipher} · {wlan.SecurityAdvice}";
        SsidText.Text = wlan.Ssid;
        IpText.Text = wlan.IpAddress;
        GatewayText.Text = wlan.Gateway;
        AdapterText.Text = wlan.AdapterName;
        BssidText.Text = $"{wlan.Bssid} / {wlan.MacAddress}";
        BandChannelText.Text = $"{wlan.Band} / {wlan.Channel} · {wlan.PhyType}";
        LinkRxText.Text = Formatting.Number(wlan.ReceiveMbps, "Mbps", 0);
        LinkTxText.Text = Formatting.Number(wlan.TransmitMbps, "Mbps", 0);
        TrafficText.Text = $"↓ {wlan.DownloadMbps:0.0} Mbps   ↑ {wlan.UploadMbps:0.0} Mbps";
        TotalTrafficText.Text =
            $"累计 ↓ {Formatting.Bytes(wlan.ReceivedBytes)} · ↑ {Formatting.Bytes(wlan.SentBytes)}";
        DownloadChart.Push(wlan.DownloadMbps);
        UploadChart.Push(wlan.UploadMbps);
    }

    private async void SpeedTestButton_Click(object sender, RoutedEventArgs e)
    {
        SpeedTestButton.IsEnabled = false;
        SpeedProgress.Value = 0;
        SpeedProgressText.Text = "0%";
        SpeedPhaseText.Text = "准备测速";
        LatencyProgressText.Text = "延迟 0%";
        DownloadProgressText.Text = "下载 0%";
        UploadProgressText.Text = "上传 0%";
        ToolStatusText.Text = "正在测速...";
        try
        {
            var progress = new Progress<SpeedTestProgress>(value =>
            {
                SpeedProgress.Value = value.OverallPercent;
                SpeedProgressText.Text = $"{value.OverallPercent:0}%";
                SpeedPhaseText.Text = value.Phase;
                LatencyProgressText.Text = $"延迟 {value.LatencyPercent:0}%";
                DownloadProgressText.Text = $"下载 {value.DownloadPercent:0}%";
                UploadProgressText.Text = $"上传 {value.UploadPercent:0}%";
                ToolStatusText.Text = value.Detail;
            });
            var result = await _speedTestService.RunAsync(progress);
            if (result.Success)
            {
                LatencyText.Text = Formatting.Number(result.LatencyMs, "ms", 0);
                SpeedDownloadText.Text = Formatting.Number(result.DownloadMbps, "Mbps", 1);
                SpeedUploadText.Text = Formatting.Number(result.UploadMbps, "Mbps", 1);
                ToolStatusText.Text = result.Message;
            }
            else
            {
                ToolStatusText.Text = result.Message;
            }
        }
        finally
        {
            SpeedTestButton.IsEnabled = true;
        }
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = false;
        LanStatusText.Text = "正在扫描同一局域网设备...";
        try
        {
            var progress = new Progress<string>(message => LanStatusText.Text = message);
            var devices = await _lanScannerService.ScanAsync(progress);
            LanGrid.ItemsSource = devices;
            LanStatusText.Text = $"发现 {devices.Count} 个 ARP/在线设备。";
        }
        catch (Exception ex)
        {
            LanStatusText.Text = $"局域网扫描失败：{ex.Message}";
        }
        finally
        {
            ScanButton.IsEnabled = true;
        }
    }

    private async void RouterButton_Click(object sender, RoutedEventArgs e)
    {
        RouterButton.IsEnabled = false;
        ToolStatusText.Text = "正在检测路由器管理入口...";
        try
        {
            var url = await _wlanService.ResolveRouterUrlAsync();
            if (string.IsNullOrWhiteSpace(url))
            {
                ToolStatusText.Text = "没有检测到默认网关。";
                return;
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
            ToolStatusText.Text = $"已打开路由器入口：{url}";
        }
        catch (Exception ex)
        {
            ToolStatusText.Text = $"路由器入口打开失败：{ex.Message}";
        }
        finally
        {
            RouterButton.IsEnabled = true;
        }
    }

    private async void OptimizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Window.GetWindow(this),
                "将刷新 DNS、清理 ARP 缓存并恢复 TCP 自动调优。不会修改 Wi-Fi 密码。继续？",
                "网络优化", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        OptimizeButton.IsEnabled = false;
        ToolStatusText.Text = "正在优化网络...";
        try
        {
            var report = await Task.Run(() => _optimizationService.Optimize());
            ToolStatusText.Text = report.Summary;
        }
        finally
        {
            OptimizeButton.IsEnabled = true;
        }
    }

    private async void ResetNetworkButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Window.GetWindow(this),
                "重置 Winsock、IPv4 和 IPv6 协议栈需要重启电脑，可能影响 VPN 或自定义网络配置。确定继续？",
                "重置网络协议栈", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        ResetNetworkButton.IsEnabled = false;
        ToolStatusText.Text = "正在重置网络协议栈...";
        try
        {
            var report = await Task.Run(() => _optimizationService.ResetNetworkStack());
            MessageBox.Show(Window.GetWindow(this), report.Summary, "网络重置",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            ResetNetworkButton.IsEnabled = true;
        }
    }
}
