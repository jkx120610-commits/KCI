using KCI.Core;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace KCI.Views;

public partial class BatteryPage : UserControl
{
    private BatteryInfo _battery = new();

    public BatteryPage()
    {
        InitializeComponent();
    }

    public void SetStaticInfo(SystemInfoService info)
    {
        _battery = info.Battery;
        BatteryNameText.Text = _battery.Present
            ? $"{_battery.Name} · OEM 报告厂商 {_battery.Manufacturer}"
            : "当前设备未检测到电池，或设备由台式机电源直接供电";
        ChargeRing.Value = _battery.ChargePercent;
        HealthRing.Value = _battery.HealthPercent;
        PowerSourceText.Text = _battery.AcOnline ? "交流电已连接" : "电池供电";
        BatteryPowerText.Text = FormatPower(_battery);
        ChargingStateText.Text = _battery.AcOnline
            ? _battery.PowerMilliWatts > 1
                ? "充电中"
                : "未充放电 / 维护模式"
            : _battery.IsDischarging || _battery.PowerMilliWatts < 0
                ? "放电中"
                : "待机";
        var runtimeMinutes = _battery.EstimatedRunTimeMinutes;
        RuntimeText.Text = runtimeMinutes is > 0 and <= 1440
            ? $"预计剩余使用时间：{runtimeMinutes.Value / 60} 小时 {runtimeMinutes.Value % 60} 分钟"
            : string.Equals(_battery.Status, "交流供电", StringComparison.OrdinalIgnoreCase)
                ? "预计剩余使用时间：当前由交流电源供电"
                : "预计剩余使用时间：--";

        var items = new List<SpecItem>
        {
            new() { Label = "OEM 报告厂商", Value = _battery.Manufacturer },
            new() { Label = "电芯制造商", Value = "BIOS 未提供" },
            new() { Label = "序列号", Value = _battery.SerialNumber },
            new() { Label = "化学类型", Value = _battery.Chemistry },
            new() { Label = "循环次数", Value = _battery.CycleCount?.ToString() ?? "未知" },
            new() { Label = "设计容量（ACPI）", Value = Formatting.Number(_battery.DesignCapacity, "mWh", 0) },
            new() { Label = "满充容量（ACPI）", Value = Formatting.Number(_battery.FullChargeCapacity, "mWh", 0) },
            new() { Label = "当前容量（实时）", Value = Formatting.Number(_battery.CurrentCapacity, "mWh", 0) },
            new() { Label = "电压", Value = Formatting.Number(_battery.Voltage, "mV", 0) },
            new() { Label = "容量口径", Value = "Windows 电池报告 / ACPI" }
        };
        BatteryItems.ItemsSource = items;
    }

    public void Refresh(Snapshot snapshot)
    {
        var charge = !double.IsNaN(snapshot.Metrics.BatteryCharge)
            ? snapshot.Metrics.BatteryCharge
            : _battery.ChargePercent;
        if (!double.IsNaN(charge))
        {
            ChargeRing.Value = charge;
            ChargeChart.Push(charge);
        }
        var temperature = !double.IsNaN(snapshot.Metrics.BatteryTemp)
            ? snapshot.Metrics.BatteryTemp
            : _battery.Temperature;
        if (!double.IsNaN(temperature))
        {
            ChargingStateText.Text += $" · {temperature:0.#} °C";
        }
        UpdatePowerEstimate(snapshot.Metrics);
    }

    private void UpdatePowerEstimate(HardwareMetrics metrics)
    {
        CpuPowerText.Text = PowerText(
            metrics.CpuPowerWatts, metrics.CpuPowerEstimated);
        GpuPowerText.Text = PowerText(
            metrics.GpuPowerWatts, metrics.GpuPowerEstimated);
        OtherPowerText.Text = PowerText(
            metrics.OtherPowerWatts, metrics.OtherPowerEstimated);

        if (double.IsNaN(metrics.EstimatedSupplyWatts))
        {
            SupplyPowerText.Text = "--";
            PowerEstimateNoteText.Text = metrics.PowerEstimateNote;
            return;
        }

        var batteryChargeWatts = _battery.IsCharging && _battery.PowerMilliWatts > 0
            ? _battery.PowerMilliWatts / 1000d
            : 0d;
        var estimatedSupply = metrics.EstimatedSupplyWatts + batteryChargeWatts;
        SupplyPowerText.Text = $"≈ {estimatedSupply:0.0} W";
        PowerEstimateNoteText.Text =
            $"{metrics.PowerEstimateNote}；" +
            (batteryChargeWatts > 0
                ? $"已加入电池充电功率 {batteryChargeWatts:0.0} W"
                : "未检测到电池充电功率，按系统用电估算") +
            "。适配器线损和转换损耗未计入。";
    }

    private static string PowerText(double value, bool estimated)
    {
        return double.IsNaN(value)
            ? "--"
            : $"{value:0.0} W{(estimated ? " 估算" : string.Empty)}";
    }

    private static string FormatPower(BatteryInfo battery)
    {
        if (double.IsNaN(battery.PowerMilliWatts))
        {
            return "系统未提供";
        }
        if (Math.Abs(battery.PowerMilliWatts) < 1)
        {
            return battery.AcOnline
                ? "0.0 W（电池未充放电；适配器输入未提供）"
                : "0.0 W";
        }
        return battery.PowerMilliWatts > 0
            ? $"{battery.PowerMilliWatts / 1000d:0.0} W（充电）"
            : $"{Math.Abs(battery.PowerMilliWatts) / 1000d:0.0} W（放电）";
    }

    private void ReportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(),
                $"KCI-battery-{DateTime.Now:yyyyMMdd-HHmmss}.html");
            var process = Process.Start(new ProcessStartInfo("powercfg.exe",
                $"/batteryreport /output \"{path}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            process?.WaitForExit(30_000);
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            else
            {
                MessageBox.Show("Windows 未生成电池报告，可能该设备没有电池。", "KCI");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "生成电池报告失败");
        }
    }
}
