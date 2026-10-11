namespace KCI.Core;

public sealed class SystemInfoCache
{
    public DateTime SavedAt { get; set; }
    public string ComputerSummary { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public string SystemManufacturer { get; set; } = string.Empty;
    public string SystemModel { get; set; } = string.Empty;
    public string Bios { get; set; } = string.Empty;
    public string BaseBoard { get; set; } = string.Empty;
    public string BootTime { get; set; } = string.Empty;
    public List<SpecItem> CpuSpecs { get; set; } = [];
    public List<SpecItem> SystemSpecs { get; set; } = [];
    public List<GpuInfo> Gpus { get; set; } = [];
    public List<DisplayInfo> Displays { get; set; } = [];
    public BatteryInfo Battery { get; set; } = new();
    public List<MemoryModuleInfo> MemoryModules { get; set; } = [];
    public long TotalMemoryBytes { get; set; }
    public List<StorageInfo> StorageDevices { get; set; } = [];
    public List<VolumeInfo> Volumes { get; set; } = [];
}
