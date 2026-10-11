using System.Diagnostics;
using System.Management;

namespace KCI.Core;

public sealed class ProcessService
{
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "registry", "idle", "smss", "csrss", "wininit", "services", "lsass",
        "winlogon", "fontdrvhost", "dwm", "svchost", "audiodg", "spoolsv", "explorer",
        "sihost", "taskhostw", "ctfmon", "startmenuexperiencehost", "shellexperiencehost",
        "searchhost", "searchapp", "securityhealthservice", "securityhealthsystray",
        "runtimebroker", "dllhost", "wmiprvse", "wudfhost", "msmpeng", "nissrv",
        "textinputhost", "applicationframehost", "systemsettings", "lockapp", "logonui",
        "winstore.app", "shellexperiencehost", "usoclient", "conhost", "taskmgr",
        "kci", "msiexec", "trustedinstaller", "tiworker", "mrt", "credentialuibroker"
    };

    private readonly Dictionary<int, TimeSpan> _lastCpuTime = [];
    private readonly Dictionary<int, DateTime> _lastSampleTime = [];
    private DateTime _lastSystemSample = DateTime.MinValue;
    private TimeSpan _lastIdleTime;
    private TimeSpan _lastKernelTime;
    private TimeSpan _lastUserTime;

    public IReadOnlyList<ProcessMetric> Sample()
    {
        var now = DateTime.UtcNow;
        var result = new List<ProcessMetric>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var pid = process.Id;
                var totalCpu = process.TotalProcessorTime;
                var cpuPercent = 0d;

                if (_lastCpuTime.TryGetValue(pid, out var previousCpu) &&
                    _lastSampleTime.TryGetValue(pid, out var previousTime))
                {
                    var elapsedMs = Math.Max(1, (now - previousTime).TotalMilliseconds);
                    var cpuDeltaMs = Math.Max(0, (totalCpu - previousCpu).TotalMilliseconds);
                    cpuPercent = cpuDeltaMs / elapsedMs / Environment.ProcessorCount * 100d;
                }

                _lastCpuTime[pid] = totalCpu;
                _lastSampleTime[pid] = now;

                var name = process.ProcessName;

                result.Add(new ProcessMetric
                {
                    Pid = pid,
                    Name = name,
                    Description = DescribeProcess(name),
                    Path = string.Empty,
                    CpuPercent = Math.Clamp(cpuPercent, 0, 100),
                    MemoryMb = process.WorkingSet64 / 1024d / 1024d,
                    IsProtected = IsProtected(process)
                });
            }
            catch
            {
                // Process may exit while it is being inspected.
            }
            finally
            {
                process.Dispose();
            }
        }

        var livePids = result.Select(item => item.Pid).ToHashSet();
        foreach (var stalePid in _lastCpuTime.Keys.Where(pid => !livePids.Contains(pid)).ToArray())
        {
            _lastCpuTime.Remove(stalePid);
            _lastSampleTime.Remove(stalePid);
        }
        return result;
    }

    public double GetSystemCpuLoad()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return double.NaN;
        }

        var idleTime = ToTimeSpan(idle);
        var kernelTime = ToTimeSpan(kernel);
        var userTime = ToTimeSpan(user);
        if (_lastSystemSample == DateTime.MinValue)
        {
            _lastSystemSample = DateTime.UtcNow;
            _lastIdleTime = idleTime;
            _lastKernelTime = kernelTime;
            _lastUserTime = userTime;
            return double.NaN;
        }

        var idleDelta = idleTime - _lastIdleTime;
        var kernelDelta = kernelTime - _lastKernelTime;
        var userDelta = userTime - _lastUserTime;
        _lastIdleTime = idleTime;
        _lastKernelTime = kernelTime;
        _lastUserTime = userTime;
        _lastSystemSample = DateTime.UtcNow;

        var total = kernelDelta + userDelta;
        if (total.TotalMilliseconds <= 0)
        {
            return double.NaN;
        }
        return Math.Clamp((total - idleDelta).TotalMilliseconds / total.TotalMilliseconds * 100d, 0, 100);
    }

    public static bool IsProtected(Process process)
    {
        try
        {
            return process.SessionId == 0 || ProtectedNames.Contains(process.ProcessName);
        }
        catch
        {
            return true;
        }
    }

    public static string GetProcessOwner(int pid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (ManagementObject process in searcher.Get())
            {
                var ownerData = process.InvokeMethod("GetOwner", null, null) as ManagementBaseObject;
                var owner = ownerData?["User"]?.ToString();
                if (!string.IsNullOrWhiteSpace(owner))
                {
                    return owner;
                }
            }
        }
        catch
        {
            // Owner lookup can fail for protected processes.
        }
        return "未知";
    }

    private static TimeSpan ToTimeSpan(long value) => TimeSpan.FromTicks(value);

    private static string DescribeProcess(string processName) => processName.ToLowerInvariant() switch
    {
        "dotnet" => ".NET Host",
        "node" => "Node.js JavaScript Runtime",
        "chatgpt" => "Codex",
        "codex" => "Codex",
        "pwsh" or "powershell" => "PowerShell",
        "explorer" => "Windows 资源管理器",
        "chrome" => "Google Chrome",
        "msedge" => "Microsoft Edge",
        "msedgewebview2" => "Microsoft Edge WebView2",
        "wechat" => "微信",
        "qq" => "QQ",
        "douyin" => "抖音",
        _ => processName
    };

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool GetSystemTimes(
            out long idleTime,
            out long kernelTime,
            out long userTime);
    }
}
