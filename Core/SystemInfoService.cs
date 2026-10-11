using Microsoft.Win32;
using System.Globalization;
using System.Management;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KCI.Core;

public sealed class SystemInfoService
{
    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KCI", "system-info-cache.json");
    private bool _batteryReportRead;
    private DateTime? _cacheSavedAt;
    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        WriteIndented = false
    };

    public string LastCacheError { get; private set; } = string.Empty;
    public bool ShouldRefreshCache =>
        !_cacheSavedAt.HasValue ||
        DateTime.UtcNow - _cacheSavedAt.Value > TimeSpan.FromHours(24);

    public string ComputerSummary { get; private set; } = "正在读取主机信息";
    public string OperatingSystem { get; private set; } = "正在读取系统信息";
    public string SystemManufacturer { get; private set; } = "未知";
    public string SystemModel { get; private set; } = "未知";
    public string Bios { get; private set; } = "未知";
    public string BaseBoard { get; private set; } = "未知";
    public string BootTime { get; private set; } = "未知";
    public List<SpecItem> CpuSpecs { get; } = [];
    public List<SpecItem> SystemSpecs { get; } = [];
    public List<GpuInfo> Gpus { get; } = [];
    public List<DisplayInfo> Displays { get; } = [];
    public BatteryInfo Battery { get; private set; } = new();
    public List<MemoryModuleInfo> MemoryModules { get; } = [];
    public long TotalMemoryBytes { get; private set; }
    public List<StorageInfo> StorageDevices { get; } = [];
    public List<VolumeInfo> Volumes { get; } = [];
    public List<InstalledAppInfo> InstalledApps { get; private set; } = [];
    public GpuInfo? PrimaryGpu => Gpus
        .OrderByDescending(gpu => gpu.DedicatedMemoryBytes)
        .FirstOrDefault();

    public void Refresh()
    {
        LoadSystem();
        LoadCpu();
        LoadGpu();
        LoadDisplays();
        LoadBattery();
        LoadMemory();
        LoadStorage();
    }

    public bool LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath))
            {
                return false;
            }

            var cache = JsonSerializer.Deserialize<SystemInfoCache>(
                File.ReadAllText(CachePath), CacheJsonOptions);
            if (cache is null ||
                cache.SavedAt == default ||
                DateTime.UtcNow - cache.SavedAt > TimeSpan.FromDays(7))
            {
                return false;
            }

            ComputerSummary = cache.ComputerSummary;
            OperatingSystem = cache.OperatingSystem;
            SystemManufacturer = cache.SystemManufacturer;
            SystemModel = cache.SystemModel;
            Bios = cache.Bios;
            BaseBoard = cache.BaseBoard;
            BootTime = cache.BootTime;
            TotalMemoryBytes = cache.TotalMemoryBytes;
            ReplaceList(CpuSpecs, cache.CpuSpecs);
            ReplaceList(SystemSpecs, cache.SystemSpecs);
            ReplaceList(Gpus, cache.Gpus);
            ReplaceList(Displays, cache.Displays);
            ReplaceList(MemoryModules, cache.MemoryModules);
            ReplaceList(StorageDevices, cache.StorageDevices);
            ReplaceList(Volumes, cache.Volumes);
            Battery = cache.Battery ?? new BatteryInfo();
            _cacheSavedAt = cache.SavedAt;
            return true;
        }
        catch (Exception ex)
        {
            LastCacheError = ex.ToString();
            return false;
        }
    }

    public void SaveCache()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            var cache = new SystemInfoCache
            {
                SavedAt = DateTime.UtcNow,
                ComputerSummary = ComputerSummary,
                OperatingSystem = OperatingSystem,
                SystemManufacturer = SystemManufacturer,
                SystemModel = SystemModel,
                Bios = Bios,
                BaseBoard = BaseBoard,
                BootTime = BootTime,
                TotalMemoryBytes = TotalMemoryBytes,
                CpuSpecs = CpuSpecs.ToList(),
                SystemSpecs = SystemSpecs.ToList(),
                Gpus = Gpus.ToList(),
                Displays = Displays.ToList(),
                Battery = Battery,
                MemoryModules = MemoryModules.ToList(),
                StorageDevices = StorageDevices.ToList(),
                Volumes = Volumes.ToList()
            };
            File.WriteAllText(CachePath, JsonSerializer.Serialize(cache, CacheJsonOptions));
        }
        catch (Exception ex)
        {
            LastCacheError = ex.ToString();
        }
    }

    private static void ReplaceList<T>(ICollection<T> target, IEnumerable<T>? source)
    {
        target.Clear();
        if (source is null)
        {
            return;
        }
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    public void LoadInstalledApps()
    {
        InstalledApps = InstalledAppService.GetInstalledApps();
    }

    private void LoadSystem()
    {
        var system = QueryFirst("SELECT * FROM Win32_ComputerSystem");
        var os = QueryFirst("SELECT * FROM Win32_OperatingSystem");
        var bios = QueryFirst("SELECT * FROM Win32_BIOS");
        var board = QueryFirst("SELECT * FROM Win32_BaseBoard");

        SystemManufacturer = Text(system, "Manufacturer");
        SystemModel = Text(system, "Model");
        ComputerSummary = $"{SystemManufacturer} {SystemModel}".Trim();
        if (string.IsNullOrWhiteSpace(ComputerSummary))
        {
            ComputerSummary = Environment.MachineName;
        }

        OperatingSystem = $"{Text(os, "Caption")} {Text(os, "Version")} · {Text(os, "OSArchitecture")}";
        Bios = $"{Text(bios, "Manufacturer")} {Text(bios, "SMBIOSBIOSVersion")} ({FormatWmiDate(bios?["ReleaseDate"])})";
        BaseBoard = $"{Text(board, "Manufacturer")} {Text(board, "Product")}";
        BootTime = os?["LastBootUpTime"] is string boot
            ? ManagementDateTimeConverter.ToDateTime(boot).ToString("yyyy-MM-dd HH:mm:ss")
            : "未知";
        TotalMemoryBytes = system?["TotalPhysicalMemory"] is not null
            ? Convert.ToInt64(system["TotalPhysicalMemory"])
            : 0;

        SystemSpecs.Clear();
        SystemSpecs.Add(new SpecItem { Group = "主机", Label = "设备名称", Value = Environment.MachineName });
        SystemSpecs.Add(new SpecItem { Group = "主机", Label = "制造商 / 型号", Value = ComputerSummary });
        SystemSpecs.Add(new SpecItem { Group = "主机", Label = "主板", Value = BaseBoard });
        SystemSpecs.Add(new SpecItem { Group = "主机", Label = "BIOS", Value = Bios });
        SystemSpecs.Add(new SpecItem { Group = "系统", Label = "操作系统", Value = OperatingSystem });
        SystemSpecs.Add(new SpecItem { Group = "系统", Label = "启动时间", Value = BootTime });
        SystemSpecs.Add(new SpecItem
        {
            Group = "系统",
            Label = "物理内存",
            Value = TotalMemoryBytes > 0 ? Formatting.Bytes(TotalMemoryBytes) : "未知"
        });
    }

    private void LoadCpu()
    {
        CpuSpecs.Clear();
        var cpu = QueryFirst("SELECT * FROM Win32_Processor");
        if (cpu is not null)
        {
            CpuSpecs.Add(new SpecItem { Group = "处理器", Label = "型号", Value = Text(cpu, "Name") });
            CpuSpecs.Add(new SpecItem { Group = "处理器", Label = "制造商", Value = Text(cpu, "Manufacturer") });
            CpuSpecs.Add(new SpecItem { Group = "处理器", Label = "插槽", Value = Text(cpu, "SocketDesignation") });
            CpuSpecs.Add(new SpecItem
            {
                Group = "处理器",
                Label = "核心 / 线程",
                Value = $"{cpu["NumberOfCores"]} 核 / {cpu["NumberOfLogicalProcessors"]} 线程"
            });
            CpuSpecs.Add(new SpecItem
            {
                Group = "频率",
                Label = "当前 / 最大频率",
                Value = $"{cpu["CurrentClockSpeed"]} MHz / {cpu["MaxClockSpeed"]} MHz"
            });
            CpuSpecs.Add(new SpecItem
            {
                Group = "缓存",
                Label = "L2 / L3",
                Value = $"{FormatCache(cpu["L2CacheSize"])} / {FormatCache(cpu["L3CacheSize"])}"
            });
            CpuSpecs.Add(new SpecItem
            {
                Group = "指令集",
                Label = "架构 / 虚拟化",
                Value = $"{ArchitectureText(cpu["Architecture"])} / {BoolText(cpu["VirtualizationFirmwareEnabled"])}"
            });
            CpuSpecs.Add(new SpecItem
            {
                Group = "标识",
                Label = "Processor ID",
                Value = Text(cpu, "ProcessorId")
            });
        }

        using var key = Registry.LocalMachine.OpenSubKey(
            @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        if (key is not null)
        {
            CpuSpecs.Add(new SpecItem
            {
                Group = "注册表",
                Label = "处理器标识",
                Value = key.GetValue("Identifier")?.ToString() ?? "未知"
            });
        }
    }

    private void LoadGpu()
    {
        Gpus.Clear();
        var dxgiAdapters = DxgiService.GetAdapters();
        foreach (var item in Query("SELECT * FROM Win32_VideoController"))
        {
            var name = Text(item, "Name");
            var dxgi = dxgiAdapters.FirstOrDefault(adapter =>
                adapter.Name.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(adapter.Name, StringComparison.OrdinalIgnoreCase));
            var dedicatedMemory = dxgi?.DedicatedVideoMemory ?? 0;
            if (dedicatedMemory == 0)
            {
                dedicatedMemory = (ulong)Math.Max(0, GetGpuMemoryFromRegistry(name));
            }
            if (dedicatedMemory == 0 && item["AdapterRAM"] is not null)
            {
                dedicatedMemory = (ulong)Math.Max(0, Convert.ToInt64(item["AdapterRAM"]));
            }

            var sharedMemory = dxgi?.SharedSystemMemory ?? 0;
            if (sharedMemory == 0 && dedicatedMemory > 0)
            {
                sharedMemory = (ulong)Math.Max(0, TotalMemoryBytes / 2);
            }
            var totalMemory = dedicatedMemory + sharedMemory +
                              (dxgi?.DedicatedSystemMemory ?? 0);

            Gpus.Add(new GpuInfo
            {
                Name = name,
                Vendor = Text(item, "AdapterCompatibility"),
                DriverVersion = Text(item, "DriverVersion"),
                DriverDate = item["DriverDate"] is string date
                    ? ManagementDateTimeConverter.ToDateTime(date).ToString("yyyy-MM-dd")
                    : "未知",
                VideoProcessor = Text(item, "VideoProcessor"),
                AdapterRam = dedicatedMemory > 0
                    ? $"{FormatMemoryWithRoundedTotal(dedicatedMemory)} 专用"
                    : "由系统内存动态共享",
                SharedRam = sharedMemory > 0
                    ? $"{FormatMemoryWithRoundedTotal(sharedMemory)} 共享"
                    : "未知",
                TotalRam = totalMemory > 0
                    ? $"{FormatMemoryWithRoundedTotal(totalMemory)} 总计"
                    : "未知",
                DedicatedMemoryBytes = dedicatedMemory,
                TotalMemoryBytes = totalMemory,
                Resolution = $"{item["CurrentHorizontalResolution"] ?? "--"} × {item["CurrentVerticalResolution"] ?? "--"} @ {item["CurrentRefreshRate"] ?? "--"} Hz",
                Status = Text(item, "Status"),
                LuidLow = dxgi?.LuidLowPart ?? 0,
                LuidHigh = dxgi?.LuidHighPart ?? 0
            });
        }
    }

    private void LoadDisplays()
    {
        Displays.Clear();
        var endpoints = DisplayConfigService.GetActiveDisplays()
            .GroupBy(endpoint => endpoint.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        var index = 0;
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (monitor, _, _, _) =>
            {
                var info = new NativeMethods.MonitorInfoEx
                {
                    Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfoEx>()
                };
                if (!NativeMethods.GetMonitorInfoEx(monitor, ref info))
                {
                    return true;
                }

                var device = new NativeMethods.DevMode
                {
                    Size = (short)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.DevMode>()
                };
                var deviceName = info.DeviceName ?? string.Empty;
                endpoints.TryGetValue(deviceName, out var endpoint);
                var resolution = $"{info.Monitor.Width} × {info.Monitor.Height}";
                var refresh = "未知";
                var colorDepth = "未知";
                var monitorName = !string.IsNullOrWhiteSpace(endpoint?.FriendlyName)
                    ? endpoint.FriendlyName
                    : FindMonitorName(index);
                var interfaceType = endpoint?.InterfaceType ?? "系统未报告";

                if (!string.IsNullOrWhiteSpace(deviceName) &&
                    NativeMethods.EnumDisplaySettings(deviceName,
                        NativeMethods.EnumCurrentSettings, ref device))
                {
                    resolution = $"{device.PelsWidth} × {device.PelsHeight}";
                    refresh = $"{device.DisplayFrequency} Hz";
                    colorDepth = $"{device.BitsPerPel} bit";
                }

                var dpiText = "未知";
                if (NativeMethods.GetDpiForMonitor(monitor,
                        NativeMethods.MonitorDpiType.EffectiveDpi, out var dpiX, out _) == 0)
                {
                    dpiText = $"{dpiX} DPI ({dpiX / 96d * 100:0}%)";
                }

                Displays.Add(new DisplayInfo
                {
                    Name = string.IsNullOrWhiteSpace(monitorName) ? $"显示器 {index + 1}" : monitorName,
                    DeviceName = deviceName,
                    Resolution = resolution,
                    RefreshRate = refresh,
                    ColorDepth = colorDepth,
                    WorkArea = $"{info.WorkArea.Width} × {info.WorkArea.Height}",
                    Dpi = dpiText,
                    InterfaceType = interfaceType,
                    IsPrimary = (info.Flags & NativeMethods.MonitorInfoEx.Primary) != 0
                });
                index++;
                return true;
            }, IntPtr.Zero);

        if (Displays.Count == 0 && Gpus.Count > 0)
        {
            Displays.Add(new DisplayInfo
            {
                Name = "主显示器",
                Resolution = Gpus[0].Resolution,
                RefreshRate = "见显卡参数"
            });
        }
    }

    private static string FindMonitorName(int index)
    {
        try
        {
            using var monitorKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Enum\DISPLAY");
            if (monitorKey is null)
            {
                return string.Empty;
            }

            var displayIds = monitorKey.GetSubKeyNames().OrderBy(name => name).ToArray();
            for (var displayIndex = 0; displayIndex < displayIds.Length; displayIndex++)
            {
                using var displayKey = monitorKey.OpenSubKey(displayIds[displayIndex]);
                if (displayKey is null)
                {
                    continue;
                }

                foreach (var instanceName in displayKey.GetSubKeyNames())
                {
                    using var instanceKey = displayKey.OpenSubKey(instanceName);
                    using var parametersKey = instanceKey?.OpenSubKey("Device Parameters");
                    if (parametersKey?.GetValue("EDID") is not byte[] edid || edid.Length < 128)
                    {
                        continue;
                    }

                    if (displayIndex == index)
                    {
                        var decoded = DecodeEdidName(edid);
                        if (!string.IsNullOrWhiteSpace(decoded))
                        {
                            return decoded;
                        }
                    }
                }
            }
        }
        catch
        {
            // Fall through to the generic display label.
        }
        return string.Empty;
    }

    private static string DecodeEdidName(byte[] edid)
    {
        var manufacturerCode = ((edid[8] << 8) | edid[9]);
        var manufacturer = new string(
        [
            (char)(((manufacturerCode >> 10) & 0x1F) + 'A' - 1),
            (char)(((manufacturerCode >> 5) & 0x1F) + 'A' - 1),
            (char)((manufacturerCode & 0x1F) + 'A' - 1)
        ]);
        var year = 1990 + edid[17];
        var productName = string.Empty;

        for (var offset = 54; offset <= 108; offset += 18)
        {
            if (edid[offset] != 0 || edid[offset + 1] != 0 ||
                edid[offset + 2] != 0 || edid[offset + 3] != 0xFC)
            {
                continue;
            }

            var raw = System.Text.Encoding.ASCII.GetString(edid, offset + 5, 13);
            productName = raw.Trim('\0', ' ', '\r', '\n', '\u00A0');
            if (!string.IsNullOrWhiteSpace(productName))
            {
                break;
            }
        }

        return string.IsNullOrWhiteSpace(productName)
            ? $"{manufacturer} 显示器 ({year})"
            : $"{manufacturer} {productName} ({year})";
    }

    private void LoadBattery()
    {
        var battery = new BatteryInfo();
        using var win32Battery = QueryFirst("SELECT * FROM Win32_Battery");
        if (win32Battery is not null)
        {
            battery.Present = true;
            battery.Name = Text(win32Battery, "Name");
            battery.DeviceId = Text(win32Battery, "DeviceID");
            battery.ChargePercent = Number(win32Battery, "EstimatedChargeRemaining");
            battery.Voltage = Number(win32Battery, "DesignVoltage");
            battery.Chemistry = ChemistryText(win32Battery["Chemistry"]);
            battery.EstimatedRunTimeMinutes = TryInt(win32Battery["EstimatedRunTime"]);
            battery.Status = BatteryStatusText(TryInt(win32Battery["BatteryStatus"]));
        }

        TryReadBatteryWmi(battery);
        TryReadNativeBatteryState(battery);
        UpdateBatteryPower(battery);
        if (!battery.Present && battery.ChargePercent >= 0)
        {
            battery.Present = true;
        }
        Battery = battery;
    }

    public void RefreshBatteryDynamic()
    {
        if (!Battery.Present)
        {
            return;
        }

        if (!_batteryReportRead)
        {
            _batteryReportRead = true;
            TryApplyBatteryReport();
        }

        var win32Battery = QueryFirst("SELECT * FROM Win32_Battery");
        if (win32Battery is not null)
        {
            Battery.ChargePercent = PreferNumber(
                Number(win32Battery, "EstimatedChargeRemaining"), Battery.ChargePercent);
            Battery.Voltage = PreferNumber(
                Number(win32Battery, "DesignVoltage"), Battery.Voltage);
            Battery.EstimatedRunTimeMinutes = TryInt(win32Battery["EstimatedRunTime"]);
            Battery.Status = BatteryStatusText(TryInt(win32Battery["BatteryStatus"]));
        }

        using var fullCharge = QueryFirst("SELECT * FROM BatteryFullChargedCapacity", @"root\wmi");
        if (fullCharge is not null)
        {
            Battery.FullChargeCapacity = PreferNumber(
                Number(fullCharge, "FullChargedCapacity"), Battery.FullChargeCapacity);
        }

        using var status = QueryFirst("SELECT * FROM BatteryStatus", @"root\wmi");
        if (status is not null)
        {
            Battery.CurrentCapacity = PreferNumber(
                Number(status, "RemainingCapacity"), Battery.CurrentCapacity);
            Battery.Voltage = PreferNumber(Number(status, "Voltage"), Battery.Voltage);
            Battery.ChargeRate = Number(status, "ChargeRate");
            Battery.DischargeRate = Number(status, "DischargeRate");
            var charging = TryInt(status["Charging"]);
            var discharging = TryInt(status["Discharging"]);
            if (charging == 1)
            {
                Battery.Status = "充电中";
            }
            else if (discharging == 1)
            {
                Battery.Status = "放电中";
            }
            Battery.IsCharging = charging == 1;
            Battery.IsDischarging = discharging == 1;
        }

        using var temperature = QueryFirst("SELECT * FROM BatteryTemperature", @"root\wmi");
        if (temperature is not null)
        {
            Battery.Temperature = PreferNumber(
                Number(temperature, "Temperature"), Battery.Temperature);
        }

        if (Battery.DesignCapacity > 0 && Battery.FullChargeCapacity > 0)
        {
            Battery.HealthPercent = Math.Clamp(
                Battery.FullChargeCapacity / Battery.DesignCapacity * 100d, 0, 150);
        }
        TryReadNativeBatteryState(Battery);
        UpdateBatteryPower(Battery);
    }

    private void TryApplyBatteryReport()
    {
        var reportPath = Path.Combine(Path.GetTempPath(),
            $"KCI-battery-audit-{Environment.ProcessId}.html");
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powercfg.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "/batteryreport", "/output", reportPath }
            });
            if (process is null || !process.WaitForExit(20_000) || !File.Exists(reportPath))
            {
                return;
            }

            var html = File.ReadAllText(reportPath);
            var text = System.Net.WebUtility.HtmlDecode(
                System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " "));
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
            var match = System.Text.RegularExpressions.Regex.Match(text,
                @"BATTERY\s+1\s+NAME\s+(?<name>.+?)\s+MANUFACTURER\s+(?<manufacturer>.+?)\s+SERIAL NUMBER\s+(?<serial>.+?)\s+CHEMISTRY\s+(?<chemistry>.+?)\s+DESIGN CAPACITY\s+(?<design>[\d,]+)\s*mWh\s+FULL CHARGE CAPACITY\s+(?<full>[\d,]+)\s*mWh\s+CYCLE COUNT\s+(?<cycles>\d+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return;
            }

            Battery.Name = PreferReportText(match.Groups["name"].Value, Battery.Name);
            Battery.Manufacturer = PreferReportText(
                match.Groups["manufacturer"].Value, Battery.Manufacturer);
            Battery.SerialNumber = PreferReportText(
                match.Groups["serial"].Value, Battery.SerialNumber);
            Battery.Chemistry = PreferReportText(
                match.Groups["chemistry"].Value, Battery.Chemistry);
            Battery.DesignCapacity = ParseReportNumber(
                match.Groups["design"].Value, Battery.DesignCapacity);
            Battery.FullChargeCapacity = ParseReportNumber(
                match.Groups["full"].Value, Battery.FullChargeCapacity);
            Battery.CycleCount = (int)ParseReportNumber(
                match.Groups["cycles"].Value, Battery.CycleCount ?? 0);
            if (Battery.DesignCapacity > 0 && Battery.FullChargeCapacity > 0)
            {
                Battery.HealthPercent = Math.Clamp(
                    Battery.FullChargeCapacity / Battery.DesignCapacity * 100d, 0, 150);
            }
        }
        catch
        {
            // The WMI/ACPI values remain available if the report cannot be generated.
        }
        finally
        {
            try
            {
                if (File.Exists(reportPath))
                {
                    File.Delete(reportPath);
                }
            }
            catch
            {
                // A temporary report left by another process is harmless.
            }
        }
    }

    private static string PreferReportText(string value, string fallback)
    {
        var clean = value.Trim();
        return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
    }

    private static double ParseReportNumber(string value, double fallback)
    {
        return double.TryParse(value.Replace(",", string.Empty), out var parsed)
            ? parsed
            : fallback;
    }

    private static void TryReadBatteryWmi(BatteryInfo battery)
    {
        using var staticData = QueryFirst("SELECT * FROM BatteryStaticData", @"root\wmi");
        using var fullCharge = QueryFirst("SELECT * FROM BatteryFullChargedCapacity", @"root\wmi");
        using var status = QueryFirst("SELECT * FROM BatteryStatus", @"root\wmi");
        using var cycle = QueryFirst("SELECT * FROM BatteryCycleCount", @"root\wmi");
        using var temperature = QueryFirst("SELECT * FROM BatteryTemperature", @"root\wmi");

        if (staticData is not null)
        {
            battery.Present = true;
            battery.Name = FirstNonEmpty(Text(staticData, "DeviceName"), battery.Name);
            battery.Manufacturer = Text(staticData, "ManufactureName");
            battery.Chemistry = BatteryChemistryText(staticData["Chemistry"], battery.Chemistry);
            battery.SerialNumber = Text(staticData, "SerialNumber");
            battery.DesignCapacity = Number(staticData, "DesignedCapacity");
        }
        if (fullCharge is not null)
        {
            battery.FullChargeCapacity = Number(fullCharge, "FullChargedCapacity");
        }
        if (status is not null)
        {
            battery.CurrentCapacity = Number(status, "RemainingCapacity");
            battery.Voltage = PreferNumber(Number(status, "Voltage"), battery.Voltage);
            battery.ChargeRate = Number(status, "ChargeRate");
            battery.DischargeRate = Number(status, "DischargeRate");
            var charging = TryInt(status["Charging"]);
            var discharging = TryInt(status["Discharging"]);
            if (charging == 1)
            {
                battery.Status = "充电中";
            }
            else if (discharging == 1)
            {
                battery.Status = "放电中";
            }
            battery.IsCharging = charging == 1;
            battery.IsDischarging = discharging == 1;
        }
        if (cycle is not null)
        {
            battery.CycleCount = TryInt(cycle["CycleCount"]);
        }
        if (temperature is not null)
        {
            battery.Temperature = Number(temperature, "Temperature");
        }

        if (battery.DesignCapacity > 0 && battery.FullChargeCapacity > 0)
        {
            battery.HealthPercent = Math.Clamp(
                battery.FullChargeCapacity / battery.DesignCapacity * 100d, 0, 150);
        }
    }

    private static void TryReadNativeBatteryState(BatteryInfo battery)
    {
        try
        {
            var state = new NativeMethods.SystemBatteryState
            {
                Spare = new byte[3]
            };
            var size = (uint)System.Runtime.InteropServices.Marshal
                .SizeOf<NativeMethods.SystemBatteryState>();
            if (NativeMethods.CallNtPowerInformation(
                    NativeMethods.SystemBatteryStateInformation,
                    IntPtr.Zero,
                    0,
                    out state,
                    size) != 0)
            {
                return;
            }

            battery.AcOnline = state.AcOnLine != 0;
            battery.Present |= state.BatteryPresent != 0;
            battery.IsCharging = state.Charging != 0;
            battery.IsDischarging = state.Discharging != 0;
            if (state.MaxCapacity > 0 && battery.FullChargeCapacity <= 0)
            {
                battery.FullChargeCapacity = state.MaxCapacity;
            }
            if (state.RemainingCapacity > 0 && battery.CurrentCapacity <= 0)
            {
                battery.CurrentCapacity = state.RemainingCapacity;
            }
            if (state.Rate != 0)
            {
                battery.PowerMilliWatts = state.Rate;
            }
            if (state.EstimatedTime is > 0 and < 4_294_967_295)
            {
                battery.EstimatedRunTimeMinutes = (int)(state.EstimatedTime / 60);
            }
        }
        catch
        {
            // Native battery state is optional on desktops and virtual machines.
        }
    }

    private static void UpdateBatteryPower(BatteryInfo battery)
    {
        if (!double.IsNaN(battery.PowerMilliWatts) && battery.PowerMilliWatts != 0)
        {
            return;
        }

        if (battery.ChargeRate > 0)
        {
            battery.PowerMilliWatts = battery.ChargeRate;
            battery.IsCharging = true;
        }
        else if (battery.DischargeRate > 0)
        {
            battery.PowerMilliWatts = -battery.DischargeRate;
            battery.IsDischarging = true;
        }
        else
        {
            battery.PowerMilliWatts = battery.AcOnline ? 0 : double.NaN;
        }
    }

    private void LoadMemory()
    {
        MemoryModules.Clear();
        foreach (var item in Query("SELECT * FROM Win32_PhysicalMemory"))
        {
            MemoryModules.Add(new MemoryModuleInfo
            {
                Locator = Text(item, "DeviceLocator"),
                Capacity = item["Capacity"] is null ? "未知" : Formatting.Bytes(Convert.ToInt64(item["Capacity"])),
                Speed = $"{item["ConfiguredClockSpeed"] ?? item["Speed"] ?? "--"} MT/s",
                Manufacturer = Text(item, "Manufacturer"),
                PartNumber = Text(item, "PartNumber"),
                SerialNumber = Text(item, "SerialNumber"),
                Type = SmbiosMemoryType(TryInt(item["SMBIOSMemoryType"])),
                FormFactor = FormFactorText(TryInt(item["FormFactor"]))
            });
        }
    }

    private void LoadStorage()
    {
        StorageDevices.Clear();
        Volumes.Clear();
        var physicalDisks = Query("SELECT * FROM MSFT_PhysicalDisk",
            @"root\Microsoft\Windows\Storage").ToList();

        foreach (var drive in Query("SELECT * FROM Win32_DiskDrive"))
        {
            var model = Text(drive, "Model");
            var physical = physicalDisks.FirstOrDefault(item =>
                Text(item, "FriendlyName").Contains(model, StringComparison.OrdinalIgnoreCase) ||
                model.Contains(Text(item, "FriendlyName"), StringComparison.OrdinalIgnoreCase));
            var size = drive["Size"] is null ? 0L : Convert.ToInt64(drive["Size"]);
            StorageDevices.Add(new StorageInfo
            {
                Name = model,
                MediaType = MediaTypeText(physical?["MediaType"] ?? drive["MediaType"]),
                BusType = BusTypeText(TryInt(physical?["BusType"])),
                Health = HealthText(TryInt(physical?["HealthStatus"])),
                OperationalStatus = JoinStatus(physical?["OperationalStatus"]),
                Size = size > 0 ? Formatting.Bytes(size) : "未知",
                SerialNumber = Text(drive, "SerialNumber"),
                Firmware = Text(drive, "FirmwareRevision")
            });
        }

        foreach (var drive in DriveInfo.GetDrives()
                     .Where(drive => drive.IsReady &&
                                     (drive.DriveType == DriveType.Fixed ||
                                      drive.DriveType == DriveType.Removable)))
        {
            try
            {
                var total = drive.TotalSize;
                var free = drive.AvailableFreeSpace;
                var used = total - free;
                Volumes.Add(new VolumeInfo
                {
                    Drive = drive.Name.TrimEnd('\\'),
                    Label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "本地磁盘" : drive.VolumeLabel,
                    FileSystem = drive.DriveFormat,
                    Total = Formatting.Bytes(total),
                    Free = Formatting.Bytes(free),
                    Used = Formatting.Bytes(used),
                    TotalBytes = total,
                    FreeBytes = free,
                    UsedBytes = used,
                    UsagePercent = total > 0 ? used / (double)total * 100d : 0
                });
            }
            catch
            {
                // Removable drives can disappear during enumeration.
            }
        }
    }

    private static ManagementBaseObject? QueryFirst(string query, string scope = @"root\cimv2")
    {
        return Query(query, scope).FirstOrDefault();
    }

    private static IEnumerable<ManagementBaseObject> Query(string query, string scope = @"root\cimv2")
    {
        var rows = new List<ManagementBaseObject>();
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, query);
            foreach (ManagementBaseObject row in searcher.Get())
            {
                rows.Add(row);
            }
        }
        catch
        {
            // Missing WMI classes vary by Windows edition and hardware.
        }
        return rows;
    }

    private static string Text(ManagementBaseObject? item, string property)
    {
        if (item?[property] is null)
        {
            return "未知";
        }
        var value = item[property]?.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? "未知" : value;
    }

    private static double Number(ManagementBaseObject? item, string property)
    {
        if (item?[property] is null)
        {
            return double.NaN;
        }
        return double.TryParse(item[property]?.ToString(), NumberStyles.Any,
            CultureInfo.InvariantCulture, out var value)
            ? value
            : double.NaN;
    }

    private static int? TryInt(object? value)
    {
        if (value is null)
        {
            return null;
        }
        return int.TryParse(value.ToString(), out var result) ? result : null;
    }

    private static string FormatCache(object? value)
    {
        var numeric = TryInt(value) ?? 0;
        return numeric > 0 ? $"{numeric / 1024d:0.##} MB" : "未知";
    }

    private static string BoolText(object? value) =>
        value is bool boolean ? (boolean ? "支持" : "未启用") : "未知";

    private static string ArchitectureText(object? value) => TryInt(value) switch
    {
        0 => "x86",
        1 => "MIPS",
        2 => "Alpha",
        3 => "PowerPC",
        5 => "ARM",
        6 => "Itanium",
        9 => "x64",
        12 => "ARM64",
        _ => "未知"
    };

    private static string ChemistryText(object? value) => TryInt(value) switch
    {
        1 => "其他",
        2 => "未知",
        3 => "铅酸",
        4 => "镍镉",
        5 => "镍氢",
        6 => "锂离子",
        7 => "锌空气",
        8 => "锂聚合物",
        _ => "未知"
    };

    private static string BatteryChemistryText(object? value, string fallback)
    {
        var text = value?.ToString()?.Trim() ?? string.Empty;
        if (text.Contains("Li", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Lithium", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("锂", StringComparison.OrdinalIgnoreCase))
        {
            return "锂离子";
        }

        var mapped = ChemistryText(value);
        return mapped == "未知" ? fallback : mapped;
    }

    private static string BatteryStatusText(int? status) => status switch
    {
        1 => "放电中",
        2 => "交流供电",
        3 => "已充满",
        4 => "低电量",
        5 => "严重低电量",
        6 => "充电中",
        7 => "充电中",
        8 => "充电中",
        9 => "充电中",
        10 => "未定义",
        11 => "部分充电",
        _ => "未知"
    };

    private static string SmbiosMemoryType(int? value) => value switch
    {
        20 => "DDR",
        21 => "DDR2",
        24 => "DDR3",
        26 => "DDR4",
        34 => "DDR5",
        35 => "LPDDR5",
        _ => "未知"
    };

    private static string FormFactorText(int? value) => value switch
    {
        8 => "DIMM",
        12 => "SODIMM",
        13 => "SRIMM",
        _ => "未知"
    };

    private static string MediaTypeText(object? value)
    {
        var numeric = TryInt(value);
        if (numeric.HasValue)
        {
            return numeric.Value switch
            {
                3 => "HDD",
                4 => "SSD",
                5 => "SCM",
                _ => "未知"
            };
        }

        var text = value?.ToString() ?? string.Empty;
        if (text.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Solid", StringComparison.OrdinalIgnoreCase))
        {
            return "SSD";
        }
        if (text.Contains("Fixed", StringComparison.OrdinalIgnoreCase))
        {
            return "HDD / 未知";
        }
        return "未知";
    }

    private static string BusTypeText(int? value) => value switch
    {
        1 => "SCSI",
        3 => "ATA",
        7 => "USB",
        8 => "RAID",
        10 => "SAS",
        11 => "SATA",
        17 => "NVMe",
        _ => "未知"
    };

    private static string HealthText(int? value) => value switch
    {
        0 => "健康",
        1 => "警告",
        2 => "不健康",
        _ => "未知"
    };

    private static string JoinStatus(object? value)
    {
        if (value is Array array)
        {
            return string.Join(" / ", array.Cast<object>().Select(item => item.ToString()));
        }
        return value?.ToString() ?? "未知";
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && value != "未知") ?? "未知";

    private static double PreferNumber(double candidate, double fallback) =>
        !double.IsNaN(candidate) ? candidate : fallback;

    private static string FormatMemoryWithRoundedTotal(ulong bytes)
    {
        var rounded = bytes / 1024d / 1024d / 1024d;
        return $"{Formatting.Bytes((long)bytes)}（约 {rounded:0} GB）";
    }

    private static long GetGpuMemoryFromRegistry(string gpuName)
    {
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (classKey is null)
            {
                return 0;
            }

            foreach (var subKeyName in classKey.GetSubKeyNames())
            {
                using var adapterKey = classKey.OpenSubKey(subKeyName);
                var driverDescription = adapterKey?.GetValue("DriverDesc")?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(driverDescription) ||
                    (!driverDescription.Contains(gpuName, StringComparison.OrdinalIgnoreCase) &&
                     !gpuName.Contains(driverDescription, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var memory = adapterKey?.GetValue("HardwareInformation.qwMemorySize");
                if (memory is not null && long.TryParse(memory.ToString(), out var parsed) && parsed > 0)
                {
                    return parsed;
                }
            }
        }
        catch
        {
            // Older drivers may not expose a 64-bit VRAM value.
        }
        return 0;
    }

    private static string FormatWmiDate(object? value)
    {
        if (value is null)
        {
            return "未知";
        }

        try
        {
            return ManagementDateTimeConverter.ToDateTime(value.ToString()).ToString("yyyy-MM-dd");
        }
        catch
        {
            return Formatting.Compact(value.ToString(), 32);
        }
    }
}
