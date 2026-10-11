using Microsoft.Win32;
using System.Diagnostics;
using System.Text.Json;

namespace KCI.Core;

public enum WindowsUpdateBlockDuration
{
    OneMonth,
    SixMonths,
    OneYear
}

public sealed class WindowsUpdateBlockStatus
{
    public bool IsActive { get; init; }
    public bool IsExpired { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public string Text { get; init; } = "Windows 更新未由 KCI 暂停";
}

public sealed class WindowsUpdateService
{
    private const string StateDirectoryName = "KCI";
    private const string StateFileName = "WindowsUpdateBlock.json";
    private const string RestoreTaskName = @"KCI\RestoreWindowsUpdate";
    private static readonly string[] UpdateServices =
    [
        "wuauserv",
        "UsoSvc",
        "WaaSMedicSvc",
        "BITS"
    ];
    private static readonly string[] UpdateTasks =
    [
        @"\Microsoft\Windows\UpdateOrchestrator\Schedule Scan",
        @"\Microsoft\Windows\UpdateOrchestrator\Schedule Scan Static Task",
        @"\Microsoft\Windows\UpdateOrchestrator\UUS Failover",
        @"\Microsoft\Windows\UpdateOrchestrator\Reboot_AC",
        @"\Microsoft\Windows\UpdateOrchestrator\Reboot_Battery",
        @"\Microsoft\Windows\UpdateOrchestrator\Maintenance Install",
        @"\Microsoft\Windows\UpdateOrchestrator\Policy Install",
        @"\Microsoft\Windows\WindowsUpdate\Scheduled Start",
        @"\Microsoft\Windows\WindowsUpdate\Scheduled Start With Network",
        @"\Microsoft\Windows\WindowsUpdate\sih",
        @"\Microsoft\Windows\WindowsUpdate\sihboot"
    ];

    private static string StateDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        StateDirectoryName);

    private static string StatePath => Path.Combine(StateDirectory, StateFileName);

    public WindowsUpdateBlockStatus GetStatus()
    {
        try
        {
            if (!File.Exists(StatePath))
            {
                return new WindowsUpdateBlockStatus();
            }

            var state = JsonSerializer.Deserialize<WindowsUpdateBlockState>(
                File.ReadAllText(StatePath));
            if (state is null)
            {
                return new WindowsUpdateBlockStatus();
            }

            var expiresLocal = state.ExpiresAtUtc.ToLocalTime();
            var expired = DateTime.Now >= expiresLocal;
            return new WindowsUpdateBlockStatus
            {
                IsActive = !expired,
                IsExpired = expired,
                ExpiresAt = expiresLocal,
                Text = expired
                    ? $"KCI 停更期限已于 {expiresLocal:yyyy-MM-dd HH:mm} 到期，请恢复 Windows 更新"
                    : $"Windows 更新已暂停，到期时间 {expiresLocal:yyyy-MM-dd HH:mm}"
            };
        }
        catch
        {
            return new WindowsUpdateBlockStatus();
        }
    }

    public OperationReport PauseWindowsUpdate(WindowsUpdateBlockDuration duration)
    {
        var report = new OperationReport
        {
            Operation = "Windows 停止更新",
            StartedAt = DateTime.Now
        };
        if (!CleanupService.IsAdministrator())
        {
            throw new InvalidOperationException("该操作需要管理员权限。");
        }

        var expiresAt = duration switch
        {
            WindowsUpdateBlockDuration.OneMonth => DateTime.UtcNow.AddMonths(1),
            WindowsUpdateBlockDuration.SixMonths => DateTime.UtcNow.AddMonths(6),
            WindowsUpdateBlockDuration.OneYear => DateTime.UtcNow.AddYears(1),
            _ => DateTime.UtcNow.AddMonths(1)
        };

        Directory.CreateDirectory(StateDirectory);
        var state = new WindowsUpdateBlockState
        {
            StartedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = expiresAt,
            DurationText = duration switch
            {
                WindowsUpdateBlockDuration.OneMonth => "1 个月",
                WindowsUpdateBlockDuration.SixMonths => "半年",
                WindowsUpdateBlockDuration.OneYear => "1 年",
                _ => "1 个月"
            }
        };

        WritePolicyValues(expiresAt, report);
        foreach (var service in UpdateServices)
        {
            StopAndDisableService(service, report);
        }
        foreach (var task in UpdateTasks)
        {
            ChangeTaskState(task, enabled: false, report);
        }
        CreateRestoreTask(expiresAt, report);

        File.WriteAllText(StatePath, JsonSerializer.Serialize(state,
            new JsonSerializerOptions { WriteIndented = true }));

        report.FinishedAt = DateTime.Now;
        report.Summary = $"Windows 更新已暂停至 {expiresAt.ToLocalTime():yyyy-MM-dd HH:mm}，" +
                         "到期后 KCI 计划任务会自动恢复更新组件与策略。";
        ReportService.Save(report);
        return report;
    }

    public OperationReport RestoreWindowsUpdate()
    {
        var report = new OperationReport
        {
            Operation = "恢复 Windows 更新",
            StartedAt = DateTime.Now
        };
        if (!CleanupService.IsAdministrator())
        {
            throw new InvalidOperationException("该操作需要管理员权限。");
        }

        DeletePolicyValues(report);

        foreach (var service in UpdateServices)
        {
            RestoreService(service, report);
        }
        foreach (var task in UpdateTasks)
        {
            ChangeTaskState(task, enabled: true, report);
        }

        RunProcess("schtasks.exe",
        [
            "/Delete", "/TN", RestoreTaskName, "/F"
        ], report, "删除 KCI 恢复任务");

        try
        {
            if (File.Exists(StatePath))
            {
                File.Delete(StatePath);
            }
            report.Entries.Add(new ReportEntry
            {
                Target = StatePath,
                Action = "清除 KCI 停更状态",
                Result = "成功",
                Detail = string.Empty
            });
        }
        catch (Exception ex)
        {
            report.Entries.Add(new ReportEntry
            {
                Target = StatePath,
                Action = "清除 KCI 停更状态",
                Result = "失败",
                Detail = ex.Message
            });
        }

        report.FinishedAt = DateTime.Now;
        report.Summary = "Windows 更新服务、策略和系统计划任务已恢复。";
        ReportService.Save(report);
        return report;
    }

    private static void WritePolicyValues(DateTime expiresAtUtc, OperationReport report)
    {
        try
        {
            using var au = Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", true);
            au?.SetValue("NoAutoUpdate", 1, RegistryValueKind.DWord);
            au?.SetValue("AUOptions", 1, RegistryValueKind.DWord);
            au?.SetValue("NoAutoRebootWithLoggedOnUsers", 1, RegistryValueKind.DWord);

            using var update = Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", true);
            update?.SetValue("DoNotConnectToWindowsUpdateInternetLocations", 1,
                RegistryValueKind.DWord);
            update?.SetValue("DisableWindowsUpdateAccess", 1, RegistryValueKind.DWord);
            update?.SetValue("SetDisableUXWUAccess", 1, RegistryValueKind.DWord);

            using var ux = Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", true);
            var pauseStart = DateTime.UtcNow.ToString("O");
            var pauseEnd = expiresAtUtc.ToString("O");
            ux?.SetValue("PauseUpdatesStartTime", pauseStart, RegistryValueKind.String);
            ux?.SetValue("PauseUpdatesExpiryTime", pauseEnd, RegistryValueKind.String);
            ux?.SetValue("PauseFeatureUpdatesStartTime", pauseStart, RegistryValueKind.String);
            ux?.SetValue("PauseFeatureUpdatesEndTime", pauseEnd, RegistryValueKind.String);
            ux?.SetValue("PauseQualityUpdatesStartTime", pauseStart, RegistryValueKind.String);
            ux?.SetValue("PauseQualityUpdatesEndTime", pauseEnd, RegistryValueKind.String);
            ux?.SetValue("FlightSettingsMaxPauseDays", 3650, RegistryValueKind.DWord);

            report.Entries.Add(new ReportEntry
            {
                Target = "Windows Update 策略",
                Action = "写入停更策略",
                Result = "成功",
                Detail = "NoAutoUpdate / DisableWindowsUpdateAccess / Pause time"
            });
        }
        catch (Exception ex)
        {
            report.Entries.Add(new ReportEntry
            {
                Target = "Windows Update 策略",
                Action = "写入停更策略",
                Result = "失败",
                Detail = ex.Message
            });
        }
    }

    private static void DeletePolicyValues(OperationReport report)
    {
        var values = new (string Path, string Name)[]
        {
            (@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate"),
            (@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "AUOptions"),
            (@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoRebootWithLoggedOnUsers"),
            (@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DoNotConnectToWindowsUpdateInternetLocations"),
            (@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "DisableWindowsUpdateAccess"),
            (@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "SetDisableUXWUAccess"),
            (@"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "PauseUpdatesStartTime"),
            (@"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "PauseUpdatesExpiryTime"),
            (@"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "PauseFeatureUpdatesStartTime"),
            (@"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "PauseFeatureUpdatesEndTime"),
            (@"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "PauseQualityUpdatesStartTime"),
            (@"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "PauseQualityUpdatesEndTime"),
            (@"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings", "FlightSettingsMaxPauseDays")
        };

        foreach (var (path, name) in values)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path, true);
                key?.DeleteValue(name, throwOnMissingValue: false);
            }
            catch (Exception ex)
            {
                report.Entries.Add(new ReportEntry
                {
                    Target = $@"HKLM\{path}\{name}",
                    Action = "删除停更策略",
                    Result = "失败",
                    Detail = ex.Message
                });
            }
        }

        report.Entries.Add(new ReportEntry
        {
            Target = "Windows Update 策略",
            Action = "删除停更策略",
            Result = "完成",
            Detail = string.Empty
        });
    }

    private static void StopAndDisableService(string serviceName, OperationReport report)
    {
        RunProcess("sc.exe", ["stop", serviceName], report, $"停止 {serviceName}");
        RunProcess("sc.exe", ["config", serviceName, "start=", "disabled"],
            report, $"禁用 {serviceName}");
    }

    private static void RestoreService(string serviceName, OperationReport report)
    {
        var startType = serviceName.Equals("UsoSvc", StringComparison.OrdinalIgnoreCase)
            ? "auto"
            : "demand";
        RunProcess("sc.exe", ["config", serviceName, "start=", startType],
            report, $"恢复 {serviceName} 启动类型");
        RunProcess("sc.exe", ["start", serviceName], report, $"启动 {serviceName}");
    }

    private static void ChangeTaskState(
        string taskName,
        bool enabled,
        OperationReport report)
    {
        RunProcess("schtasks.exe",
        [
            "/Change", "/TN", taskName, enabled ? "/Enable" : "/Disable"
        ], report, enabled ? "启用更新计划任务" : "禁用更新计划任务");
    }

    private static void CreateRestoreTask(DateTime expiresAtUtc, OperationReport report)
    {
        var local = expiresAtUtc.ToLocalTime();
        var executable = Environment.ProcessPath ?? "KCI.exe";
        RunProcess("schtasks.exe",
        [
            "/Create",
            "/TN", RestoreTaskName,
            "/TR", $"\"{executable}\" --restore-windows-update",
            "/SC", "ONCE",
            "/ST", local.ToString("HH:mm"),
            "/SD", local.ToString("yyyy/MM/dd"),
            "/RU", "SYSTEM",
            "/RL", "HIGHEST",
            "/F"
        ], report, "创建到期恢复任务");
    }

    private static void RunProcess(
        string fileName,
        IEnumerable<string> arguments,
        OperationReport report,
        string action)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null || !process.WaitForExit(25_000))
            {
                report.Entries.Add(new ReportEntry
                {
                    Target = fileName,
                    Action = action,
                    Result = "超时",
                    Detail = string.Join(' ', arguments)
                });
                return;
            }

            var output = string.Join(" ",
                process.StandardOutput.ReadToEnd().Trim(),
                process.StandardError.ReadToEnd().Trim()).Trim();
            report.Entries.Add(new ReportEntry
            {
                Target = fileName,
                Action = action,
                Result = process.ExitCode == 0 ? "成功" : "跳过/需检查",
                Detail = string.IsNullOrWhiteSpace(output)
                    ? $"退出代码 {process.ExitCode}"
                    : Formatting.Compact(output, 180)
            });
        }
        catch (Exception ex)
        {
            report.Entries.Add(new ReportEntry
            {
                Target = fileName,
                Action = action,
                Result = "失败",
                Detail = ex.Message
            });
        }
    }

    private sealed class WindowsUpdateBlockState
    {
        public DateTime StartedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public string DurationText { get; set; } = string.Empty;
    }
}
