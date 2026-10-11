using LibreHardwareMonitor.Hardware;

namespace KCI.Core;

public sealed class HardwareMonitorEngine : IDisposable
{
    private readonly Computer _computer;
    private readonly UpdateVisitor _visitor = new();
    private readonly object _sync = new();
    private bool _opened;
    private string _sensorStatus = "传感器未启动";

    public HardwareMonitorEngine()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = false
        };
    }

    public void Open()
    {
        lock (_sync)
        {
            if (_opened)
            {
                return;
            }

            try
            {
                _computer.Open();
                _opened = true;
                _sensorStatus = "LibreHardwareMonitor 已连接";
            }
            catch (Exception ex)
            {
                _sensorStatus = $"传感器启动失败：{Formatting.Compact(ex.Message)}";
            }
        }
    }

    public HardwareMetrics Sample()
    {
        var metrics = new HardwareMetrics();
        lock (_sync)
        {
            if (!_opened)
            {
                metrics.SensorStatus = _sensorStatus;
                return metrics;
            }

            try
            {
                _computer.Accept(_visitor);
                foreach (var hardware in _computer.Hardware)
                {
                    ReadHardware(hardware, metrics);
                }
                metrics.SensorStatus = _sensorStatus;
            }
            catch (Exception ex)
            {
                metrics.SensorStatus = $"传感器读取异常：{Formatting.Compact(ex.Message)}";
            }
        }
        return metrics;
    }

    private static void ReadHardware(IHardware hardware, HardwareMetrics metrics)
    {
        switch (hardware.HardwareType)
        {
            case HardwareType.Cpu:
                metrics.CpuLoad = PreferValid(metrics.CpuLoad,
                    ReadSensor(hardware, SensorType.Load, ["CPU Total", "Total CPU"]));
                metrics.CpuTemp = PreferValid(metrics.CpuTemp,
                    ReadSensor(hardware, SensorType.Temperature,
                        ["CPU Package", "Package", "Core Average", "CPU Core"]));
                metrics.CpuPowerWatts = ReadSensor(hardware, SensorType.Power,
                    ["CPU Package", "Package", "CPU Power", "Platform"]);
                break;

            case HardwareType.GpuNvidia:
            case HardwareType.GpuAmd:
            case HardwareType.GpuIntel:
                metrics.GpuLoad = PreferValid(metrics.GpuLoad,
                    ReadSensor(hardware, SensorType.Load,
                        ["GPU Core", "D3D 3D", "Core", "GPU Utilization"]));
                metrics.GpuTemp = PreferValid(metrics.GpuTemp,
                    ReadSensor(hardware, SensorType.Temperature,
                        ["GPU Core", "GPU Temperature", "Hot Spot", "Core"]));
                metrics.GpuPowerWatts = ReadSensor(hardware, SensorType.Power,
                    ["GPU Power", "GPU Package", "Board Power", "Package"]);
                break;

            case HardwareType.Memory:
                metrics.MemoryLoad = PreferValid(metrics.MemoryLoad,
                    ReadSensor(hardware, SensorType.Load, ["Memory", "Used Space"]));
                metrics.MemoryTemp = PreferValid(metrics.MemoryTemp,
                    ReadSensor(hardware, SensorType.Temperature,
                        ["DIMM", "Memory", "SPD Hub"]));
                break;

            case HardwareType.Storage:
                var temp = ReadSensor(hardware, SensorType.Temperature,
                    ["Temperature", "Drive Temperature", "Composite"]);
                var usage = ReadSensor(hardware, SensorType.Load,
                    ["Used Space", "Total Activity", "Read Activity"]);
                if (IsValid(temp))
                {
                    metrics.StorageTemperature[hardware.Name] = temp;
                }
                if (IsValid(usage))
                {
                    metrics.StorageUsage[hardware.Name] = usage;
                }
                break;

            case HardwareType.Battery:
                metrics.BatteryCharge = PreferValid(metrics.BatteryCharge,
                    ReadSensor(hardware, SensorType.Level, ["Charge Level", "Battery"]));
                metrics.BatteryTemp = PreferValid(metrics.BatteryTemp,
                    ReadSensor(hardware, SensorType.Temperature,
                        ["Temperature", "Battery Temperature"]));
                break;

            default:
                var devicePower = ReadSensor(hardware, SensorType.Power, ["Power"]);
                if (IsValid(devicePower) &&
                    !hardware.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase) &&
                    !hardware.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase))
                {
                    metrics.OtherPowerWatts = IsValid(metrics.OtherPowerWatts)
                        ? metrics.OtherPowerWatts + devicePower
                        : devicePower;
                }
                break;
        }

        foreach (var subHardware in hardware.SubHardware)
        {
            ReadHardware(subHardware, metrics);
        }
    }

    private static double ReadSensor(IHardware hardware, SensorType type, string[] preferredNames)
    {
        var candidates = hardware.Sensors
            .Where(sensor => sensor.SensorType == type && sensor.Value.HasValue)
            .Select(sensor => new { Sensor = sensor, Value = sensor.Value!.Value })
            .ToList();

        foreach (var preferred in preferredNames)
        {
            var match = candidates.FirstOrDefault(item =>
                item.Sensor.Name.Contains(preferred, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match.Value;
            }
        }

        return candidates.Count > 0 ? candidates[0].Value : double.NaN;
    }

    private static double PreferValid(double current, double candidate)
    {
        return !IsValid(current) || (IsValid(candidate) && candidate > current)
            ? candidate
            : current;
    }

    private static bool IsValid(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);

    public void Dispose()
    {
        lock (_sync)
        {
            if (!_opened)
            {
                return;
            }

            try
            {
                _computer.Close();
            }
            catch
            {
                // Closing a hardware provider should not prevent process exit.
            }
            _opened = false;
        }
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer)
        {
            computer.Traverse(this);
        }

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var subHardware in hardware.SubHardware)
            {
                subHardware.Accept(this);
            }
        }

        public void VisitSensor(ISensor sensor)
        {
        }

        public void VisitParameter(IParameter parameter)
        {
        }
    }
}
