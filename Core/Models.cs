using System.Collections.ObjectModel;
using System.Windows.Media;

namespace KCI.Core;

public sealed class HardwareMetrics
{
    public double CpuLoad { get; set; } = double.NaN;
    public double CpuTemp { get; set; } = double.NaN;
    public double GpuLoad { get; set; } = double.NaN;
    public double GpuTemp { get; set; } = double.NaN;
    public double CpuPowerWatts { get; set; } = double.NaN;
    public double GpuPowerWatts { get; set; } = double.NaN;
    public double OtherPowerWatts { get; set; } = double.NaN;
    public bool CpuPowerEstimated { get; set; }
    public bool GpuPowerEstimated { get; set; }
    public bool OtherPowerEstimated { get; set; }
    public double EstimatedSupplyWatts { get; set; } = double.NaN;
    public string PowerEstimateNote { get; set; } = string.Empty;
    public double GpuDedicatedUsed { get; set; } = double.NaN;
    public double GpuSharedUsed { get; set; } = double.NaN;
    public double GpuTotalCommitted { get; set; } = double.NaN;
    public double MemoryLoad { get; set; } = double.NaN;
    public double MemoryTemp { get; set; } = double.NaN;
    public double BatteryTemp { get; set; } = double.NaN;
    public double BatteryCharge { get; set; } = double.NaN;
    public Dictionary<string, double> StorageTemperature { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> StorageUsage { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public string SensorStatus { get; set; } = "正在连接硬件传感器";
}

public sealed class ProcessMetric
{
    public int Pid { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public double CpuPercent { get; init; }
    public double GpuPercent { get; set; } = double.NaN;
    public double MemoryMb { get; init; }
    public bool IsProtected { get; init; }
}

public sealed class SpecItem
{
    public string Group { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

public sealed class GpuInfo
{
    public string Name { get; set; } = "未知显卡";
    public string Vendor { get; set; } = "未知";
    public string DriverVersion { get; set; } = "未知";
    public string DriverDate { get; set; } = "未知";
    public string VideoProcessor { get; set; } = "未知";
    public string AdapterRam { get; set; } = "未知";
    public string SharedRam { get; set; } = "未知";
    public string TotalRam { get; set; } = "未知";
    public ulong DedicatedMemoryBytes { get; set; }
    public ulong TotalMemoryBytes { get; set; }
    public string Resolution { get; set; } = "未知";
    public string Status { get; set; } = "未知";
    public uint LuidLow { get; set; }
    public int LuidHigh { get; set; }
}

public sealed class DisplayInfo
{
    public string Name { get; set; } = "显示器";
    public string DeviceName { get; set; } = string.Empty;
    public string Resolution { get; set; } = "未知";
    public string RefreshRate { get; set; } = "未知";
    public string ColorDepth { get; set; } = "未知";
    public string WorkArea { get; set; } = "未知";
    public string Dpi { get; set; } = "未知";
    public string InterfaceType { get; set; } = "系统未报告";
    public bool IsPrimary { get; set; }
}

public sealed class BatteryInfo
{
    public bool Present { get; set; }
    public string Name { get; set; } = "未检测到电池";
    public string DeviceId { get; set; } = "未知";
    public string Manufacturer { get; set; } = "未知";
    public string SerialNumber { get; set; } = "未知";
    public string Chemistry { get; set; } = "未知";
    public double ChargePercent { get; set; } = double.NaN;
    public double HealthPercent { get; set; } = double.NaN;
    public double DesignCapacity { get; set; } = double.NaN;
    public double FullChargeCapacity { get; set; } = double.NaN;
    public double CurrentCapacity { get; set; } = double.NaN;
    public double RawHealthPercent { get; set; } = double.NaN;
    public double Voltage { get; set; } = double.NaN;
    public double Temperature { get; set; } = double.NaN;
    public double PowerMilliWatts { get; set; } = double.NaN;
    public double ChargeRate { get; set; } = double.NaN;
    public double DischargeRate { get; set; } = double.NaN;
    public bool AcOnline { get; set; }
    public bool IsCharging { get; set; }
    public bool IsDischarging { get; set; }
    public string HealthNote { get; set; } = "等待读取容量数据";
    public int? CycleCount { get; set; }
    public int? EstimatedRunTimeMinutes { get; set; }
    public string Status { get; set; } = "未知";
}

public sealed class MemoryModuleInfo
{
    public string Locator { get; set; } = "未知";
    public string Capacity { get; set; } = "未知";
    public string Speed { get; set; } = "未知";
    public string Manufacturer { get; set; } = "未知";
    public string PartNumber { get; set; } = "未知";
    public string SerialNumber { get; set; } = "未知";
    public string Type { get; set; } = "未知";
    public string FormFactor { get; set; } = "未知";
}

public sealed class StorageInfo
{
    public string Name { get; set; } = "未知磁盘";
    public string MediaType { get; set; } = "未知";
    public string BusType { get; set; } = "未知";
    public string Health { get; set; } = "未知";
    public string OperationalStatus { get; set; } = "未知";
    public string Size { get; set; } = "未知";
    public string SerialNumber { get; set; } = "未知";
    public string Firmware { get; set; } = "未知";
    public double UsagePercent { get; set; } = double.NaN;
    public double Temperature { get; set; } = double.NaN;
    public string Volumes { get; set; } = "未分配盘符";
}

public sealed class VolumeInfo
{
    public string Drive { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string FileSystem { get; set; } = string.Empty;
    public string Total { get; set; } = string.Empty;
    public string Free { get; set; } = string.Empty;
    public string Used { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public long UsedBytes { get; set; }
    public double UsagePercent { get; set; }
}

public sealed class InstalledAppInfo
{
    public string Name { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string InstallLocation { get; set; } = string.Empty;
    public string UninstallString { get; set; } = string.Empty;
    public string QuietUninstallString { get; set; } = string.Empty;
    public string DisplayIcon { get; set; } = string.Empty;
    public string RegistryKey { get; set; } = string.Empty;
    public string RegistryView { get; set; } = string.Empty;
    public long ReportedSizeBytes { get; set; }
    public long EstimatedSizeBytes { get; set; }
    public string SizeSource { get; set; } = "未知";
    public bool IsSystemApp { get; set; }
    public bool Is360Product { get; set; }
    public bool IsSelected { get; set; }
    public ImageSource? Icon { get; set; }
    public string SizeText => EstimatedSizeBytes > 0
        ? FormatBytes(EstimatedSizeBytes)
        : "未知";

    public override string ToString() => Name;

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }
}

public sealed class AppUsageInfo
{
    public ImageSource? Icon { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public string Location { get; init; } = "系统未报告";
    public long SizeBytes { get; init; }
    public double SharePercent { get; init; }
    public string SizeText => SizeBytes > 0 ? Formatting.Bytes(SizeBytes) : "未知";
    public string ShareText => SharePercent > 0 ? $"{SharePercent:0.0}%" : "--";
    public string SizeSource { get; init; } = "未知";
}

public enum JunkItemKind
{
    File,
    DirectoryContents,
    RecycleBin
}

public sealed class JunkItem : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isSelected;

    public string Category { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Glyph { get; init; } = "\uE74D";
    public string RiskText { get; init; } = "可清理";
    public long SizeBytes { get; init; }
    public bool IsRecommended { get; init; }
    public JunkItemKind Kind { get; init; }
    public string AllowedRoot { get; init; } = string.Empty;
    public int ItemCount { get; init; }
    public string SizeText => SizeBytes > 0 ? Formatting.Bytes(SizeBytes) : "未知";
    public string PathText => Kind == JunkItemKind.RecycleBin
        ? $"包含 {ItemCount} 个项目"
        : Formatting.Compact(Path, 110);

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
}

public sealed class OperationReport
{
    public string Operation { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime FinishedAt { get; set; } = DateTime.Now;
    public string Summary { get; set; } = string.Empty;
    public List<ReportEntry> Entries { get; } = [];
    public string ReportPath { get; set; } = string.Empty;
}

public sealed class ReportEntry
{
    public string Target { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

public sealed class Snapshot
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public HardwareMetrics Metrics { get; init; } = new();
    public IReadOnlyList<ProcessMetric> Processes { get; init; } = [];
    public bool ProcessDataChanged { get; init; }
    public WlanSnapshot? Wlan { get; init; }
}

public sealed class WlanSnapshot
{
    public bool Connected { get; set; }
    public string AdapterName { get; set; } = "未检测到无线网卡";
    public string Ssid { get; set; } = "未连接";
    public string Bssid { get; set; } = "未知";
    public string Authentication { get; set; } = "未知";
    public string Cipher { get; set; } = "未知";
    public string PhyType { get; set; } = "未知";
    public string Band { get; set; } = "未知";
    public string Channel { get; set; } = "未知";
    public int SignalQuality { get; set; } = -1;
    public int Rssi { get; set; } = int.MinValue;
    public double ReceiveMbps { get; set; } = double.NaN;
    public double TransmitMbps { get; set; } = double.NaN;
    public double DownloadMbps { get; set; }
    public double UploadMbps { get; set; }
    public long ReceivedBytes { get; set; }
    public long SentBytes { get; set; }
    public string IpAddress { get; set; } = "未知";
    public string Gateway { get; set; } = "未知";
    public string MacAddress { get; set; } = "未知";
    public string SecurityLevel { get; set; } = "未知";
    public string SecurityAdvice { get; set; } = string.Empty;
    public string RouterUrl { get; set; } = string.Empty;
}

public sealed class LanDeviceInfo
{
    public string IpAddress { get; init; } = string.Empty;
    public string MacAddress { get; init; } = string.Empty;
    public string HostName { get; init; } = string.Empty;
    public string Role { get; init; } = "局域网设备";
    public string Vendor { get; init; } = "未知厂商";
    public bool Responsive { get; init; }
}

public sealed class SpeedTestResult
{
    public bool Success { get; init; }
    public double LatencyMs { get; init; } = double.NaN;
    public double DownloadMbps { get; init; } = double.NaN;
    public double UploadMbps { get; init; } = double.NaN;
    public string Message { get; init; } = string.Empty;
}

public sealed class SpeedTestProgress
{
    public string Phase { get; init; } = string.Empty;
    public double OverallPercent { get; init; }
    public double LatencyPercent { get; init; }
    public double DownloadPercent { get; init; }
    public double UploadPercent { get; init; }
    public string Detail { get; init; } = string.Empty;
}

public sealed class ProcessCollection : ObservableCollection<ProcessMetric>
{
}
