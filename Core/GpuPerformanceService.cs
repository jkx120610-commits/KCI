using System.Diagnostics;

namespace KCI.Core;

public sealed class GpuCounterSnapshot
{
    public double Utilization { get; init; } = double.NaN;
    public double DedicatedUsed { get; init; } = double.NaN;
    public double SharedUsed { get; init; } = double.NaN;
    public double TotalCommitted { get; init; } = double.NaN;
    public IReadOnlyDictionary<int, double> ProcessUsage { get; init; } =
        new Dictionary<int, double>();
}

public sealed class GpuPerformanceService : IDisposable
{
    private const string EngineCategoryName = "GPU Engine";
    private const string MemoryCategoryName = "GPU Adapter Memory";
    private readonly Dictionary<string, PerformanceCounter> _engineCounters =
        new(StringComparer.OrdinalIgnoreCase);
    private PerformanceCounter? _dedicatedUsage;
    private PerformanceCounter? _sharedUsage;
    private PerformanceCounter? _totalCommitted;
    private DateTime _lastCounterRefresh = DateTime.MinValue;

    public GpuCounterSnapshot Sample(uint luidLowPart, int luidHighPart)
    {
        var adapterToken =
            $"luid_0x{unchecked((uint)luidHighPart):X8}_0x{luidLowPart:X8}_phys_0";
        var utilization = double.NaN;
        var processTotals = new Dictionary<int, double>();

        try
        {
            RefreshEngineCounters(adapterToken);
            var engineTotals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in _engineCounters)
            {
                float value;
                try
                {
                    value = pair.Value.NextValue();
                }
                catch
                {
                    continue;
                }

                if (value <= 0)
                {
                    continue;
                }

                var pid = ParsePid(pair.Key);
                if (pid > 0)
                {
                    processTotals[pid] = Math.Clamp(
                        processTotals.GetValueOrDefault(pid) + value, 0, 100);
                }

                var engineKey = ParseEngineKey(pair.Key);
                engineTotals[engineKey] = Math.Clamp(
                    engineTotals.GetValueOrDefault(engineKey) + value, 0, 100);
            }

            utilization = engineTotals.Count > 0
                ? engineTotals.Values.Max()
                : 0d;
            utilization = Math.Clamp(utilization, 0, 100);
        }
        catch
        {
            // Perf counters can be disabled by policy; LibreHardwareMonitor remains the fallback.
        }

        return new GpuCounterSnapshot
        {
            Utilization = utilization,
            DedicatedUsed = ReadMemoryCounter(
                ref _dedicatedUsage, adapterToken, "Dedicated Usage"),
            SharedUsed = ReadMemoryCounter(
                ref _sharedUsage, adapterToken, "Shared Usage"),
            TotalCommitted = ReadMemoryCounter(
                ref _totalCommitted, adapterToken, "Total Committed"),
            ProcessUsage = processTotals
        };
    }

    private void RefreshEngineCounters(string adapterToken)
    {
        if (DateTime.UtcNow - _lastCounterRefresh < TimeSpan.FromSeconds(5))
        {
            return;
        }
        _lastCounterRefresh = DateTime.UtcNow;

        var category = new PerformanceCounterCategory(EngineCategoryName);
        var instances = category.GetInstanceNames()
            .Where(name => name.Contains(adapterToken, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var stale in _engineCounters.Keys
                     .Where(name => !instances.Contains(name))
                     .ToArray())
        {
            _engineCounters[stale].Dispose();
            _engineCounters.Remove(stale);
        }

        foreach (var instance in instances)
        {
            if (_engineCounters.ContainsKey(instance))
            {
                continue;
            }
            try
            {
                _engineCounters[instance] = new PerformanceCounter(
                    EngineCategoryName, "Utilization Percentage", instance, true);
            }
            catch
            {
                // The process may have exited between enumeration and construction.
            }
        }
    }

    private double ReadMemoryCounter(
        ref PerformanceCounter? counter,
        string adapterToken,
        string counterName)
    {
        try
        {
            if (counter is null)
            {
                counter = new PerformanceCounter(
                    MemoryCategoryName, counterName, adapterToken, true);
            }
            return counter.NextValue();
        }
        catch
        {
            return double.NaN;
        }
    }

    private static int ParsePid(string instance)
    {
        if (!instance.StartsWith("pid_", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var end = instance.IndexOf("_luid_", StringComparison.OrdinalIgnoreCase);
        return end > 4 && int.TryParse(instance.AsSpan(4, end - 4), out var pid)
            ? pid
            : 0;
    }

    private static string ParseEngineKey(string instance)
    {
        var marker = instance.IndexOf("_eng_", StringComparison.OrdinalIgnoreCase);
        var typeMarker = instance.IndexOf("_engtype_", StringComparison.OrdinalIgnoreCase);
        if (marker < 0 || typeMarker < 0)
        {
            return instance;
        }

        var engineIdEnd = typeMarker;
        var engineId = instance[(marker + 5)..engineIdEnd];
        var engineType = instance[(typeMarker + 9)..];
        return $"{engineId}:{engineType}";
    }

    public void Dispose()
    {
        foreach (var counter in _engineCounters.Values)
        {
            counter.Dispose();
        }
        _engineCounters.Clear();
        _dedicatedUsage?.Dispose();
        _sharedUsage?.Dispose();
        _totalCommitted?.Dispose();
    }
}
