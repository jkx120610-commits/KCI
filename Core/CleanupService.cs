using Microsoft.Win32;
using System.Diagnostics;
using System.Management;
using System.Security.Principal;
using System.ServiceProcess;

namespace KCI.Core;

public sealed class CleanupService
{
    private static readonly HashSet<string> AlwaysProtected = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "registry", "idle", "smss", "csrss", "wininit", "services", "lsass",
        "winlogon", "fontdrvhost", "dwm", "svchost", "audiodg", "spoolsv", "explorer",
        "sihost", "taskhostw", "ctfmon", "startmenuexperiencehost", "shellexperiencehost",
        "searchhost", "searchapp", "runtimebroker", "dllhost", "wmiprvse", "wudfhost",
        "textinputhost", "applicationframehost", "systemsettings", "lockapp", "logonui",
        "kci", "msiexec", "trustedinstaller", "tiworker"
    };

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public IReadOnlyList<ProcessMetric> GetReleasableProcesses()
    {
        var processes = new ProcessService().Sample();
        return processes
            .Where(process => !process.IsProtected && !AlwaysProtected.Contains(process.Name))
            .OrderByDescending(process => process.MemoryMb)
            .ToList();
    }

    public OperationReport ReleaseMemory(IEnumerable<ProcessMetric> selected)
    {
        var report = new OperationReport
        {
            Operation = "RAM 超级释放器",
            StartedAt = DateTime.Now
        };

        var myProcess = Process.GetCurrentProcess();
        var skipped = 0;
        foreach (var item in selected)
        {
            if (item.Pid == myProcess.Id || AlwaysProtected.Contains(item.Name))
            {
                skipped++;
                report.Entries.Add(new ReportEntry
                {
                    Target = $"{item.Name} ({item.Pid})",
                    Action = "结束进程",
                    Result = "跳过",
                    Detail = "系统保护或 KCI 自身进程"
                });
                continue;
            }

            try
            {
                using var process = Process.GetProcessById(item.Pid);
                var releasedMb = process.WorkingSet64 / 1024d / 1024d;
                process.CloseMainWindow();
                if (!process.WaitForExit(1200))
                {
                    process.Kill(true);
                    process.WaitForExit(2500);
                }
                report.Entries.Add(new ReportEntry
                {
                    Target = $"{item.Name} ({item.Pid})",
                    Action = "结束进程",
                    Result = "成功",
                    Detail = $"预计释放 {releasedMb:0.0} MB"
                });
            }
            catch (Exception ex)
            {
                report.Entries.Add(new ReportEntry
                {
                    Target = $"{item.Name} ({item.Pid})",
                    Action = "结束进程",
                    Result = "失败",
                    Detail = ex.Message
                });
            }
        }

        report.FinishedAt = DateTime.Now;
        var success = report.Entries.Count(entry => entry.Result == "成功");
        report.Summary = $"选中 {selected.Count()} 项，成功 {success} 项，保护跳过 {skipped} 项。";
        ReportService.Save(report);
        return report;
    }

    public OperationReport Uninstall360(IEnumerable<InstalledAppInfo> selected)
    {
        var report = new OperationReport
        {
            Operation = "360 全家桶卸载",
            StartedAt = DateTime.Now
        };

        Kill360Processes(report);
        StopAndDelete360Services(report);
        foreach (var app in selected.Where(app => app.Is360Product))
        {
            UninstallApplication(app, report, true);
        }

        Kill360Processes(report);
        StopAndDelete360Services(report);
        Cleanup360Paths(selected, report);
        Cleanup360Registry(report);

        report.FinishedAt = DateTime.Now;
        report.Summary = "已完成 360 组件卸载、残留服务停止和受控目录清理。";
        ReportService.Save(report);
        return report;
    }

    public OperationReport CleanApplications(IEnumerable<InstalledAppInfo> selected)
    {
        var report = new OperationReport
        {
            Operation = "SSD/HDD 超级清除",
            StartedAt = DateTime.Now
        };

        foreach (var app in selected.Where(app => !app.IsSystemApp))
        {
            UninstallApplication(app, report, false);
            CleanupResidualData(app, report);
        }

        report.FinishedAt = DateTime.Now;
        report.Summary = $"已处理 {selected.Count(app => !app.IsSystemApp)} 个应用，并扫描常见本地数据残留位置。";
        ReportService.Save(report);
        return report;
    }

    private static void UninstallApplication(
        InstalledAppInfo app,
        OperationReport report,
        bool preferQuiet360)
    {
        var command = !string.IsNullOrWhiteSpace(app.QuietUninstallString)
            ? app.QuietUninstallString
            : app.UninstallString;
        if (string.IsNullOrWhiteSpace(command))
        {
            report.Entries.Add(new ReportEntry
            {
                Target = app.Name,
                Action = "调用卸载程序",
                Result = "跳过",
                Detail = "注册表未提供卸载命令，将仅清理受控残留"
            });
            return;
        }

        var originalCommand = app.UninstallString;
        var usedSilent = preferQuiet360 &&
                         !string.IsNullOrWhiteSpace(app.QuietUninstallString);
        var succeeded = RunUninstaller(app, command, usedSilent, report);
        if (preferQuiet360 && !succeeded &&
            !string.IsNullOrWhiteSpace(originalCommand) &&
            !originalCommand.Equals(command, StringComparison.OrdinalIgnoreCase))
        {
            RunUninstaller(app, originalCommand, false, report);
        }
    }

    private static bool RunUninstaller(
        InstalledAppInfo app,
        string command,
        bool silent,
        OperationReport report)
    {
        try
        {
            var startInfo = ParseCommand(command);
            if (silent &&
                !startInfo.Arguments.Contains("/S", StringComparison.OrdinalIgnoreCase) &&
                !startInfo.Arguments.Contains("/silent", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.Arguments = string.Join(' ', startInfo.Arguments, "/S").Trim();
            }

            startInfo.UseShellExecute = !silent;
            startInfo.CreateNoWindow = silent;
            startInfo.WindowStyle = silent
                ? ProcessWindowStyle.Hidden
                : ProcessWindowStyle.Normal;
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                throw new InvalidOperationException("无法启动卸载进程");
            }

            var exited = process.WaitForExit(180_000);
            if (!exited)
            {
                report.Entries.Add(new ReportEntry
                {
                    Target = app.Name,
                    Action = silent ? "静默卸载" : "交互卸载",
                    Result = "超时",
                    Detail = $"PID {process.Id} 在 180 秒内未完成"
                });
                return false;
            }

            report.Entries.Add(new ReportEntry
            {
                Target = app.Name,
                Action = silent ? "静默卸载" : "交互卸载",
                Result = process.ExitCode == 0 ? "成功" : "需检查",
                Detail = $"退出代码 {process.ExitCode}"
            });
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            report.Entries.Add(new ReportEntry
            {
                Target = app.Name,
                Action = silent ? "静默卸载" : "交互卸载",
                Result = "失败",
                Detail = ex.Message
            });
            return false;
        }
    }

    private void Kill360Processes(OperationReport report)
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var name = process.ProcessName;
                var path = string.Empty;
                try
                {
                    path = process.MainModule?.FileName ?? string.Empty;
                }
                catch
                {
                    // Access can be denied for protected 360 services.
                }

                if (!name.Contains("360", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("Qihoo", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("ZhuDongFangYu", StringComparison.OrdinalIgnoreCase) &&
                    !path.Contains("360", StringComparison.OrdinalIgnoreCase) &&
                    !path.Contains("Qihoo", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                process.Kill(true);
                report.Entries.Add(new ReportEntry
                {
                    Target = $"{name} ({process.Id})",
                    Action = "结束 360 进程",
                    Result = "成功",
                    Detail = path
                });
            }
            catch (Exception ex)
            {
                report.Entries.Add(new ReportEntry
                {
                    Target = process.ProcessName,
                    Action = "结束 360 进程",
                    Result = "失败",
                    Detail = ex.Message
                });
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void StopAndDelete360Services(OperationReport report)
    {
        foreach (var service in ServiceController.GetServices())
        {
            try
            {
                var displayName = service.DisplayName;
                var serviceName = service.ServiceName;
                var path = ReadServicePath(serviceName);
                if (!displayName.Contains("360", StringComparison.OrdinalIgnoreCase) &&
                    !serviceName.Contains("360", StringComparison.OrdinalIgnoreCase) &&
                    !displayName.Contains("Qihoo", StringComparison.OrdinalIgnoreCase) &&
                    !serviceName.Contains("Qihoo", StringComparison.OrdinalIgnoreCase) &&
                    !path.Contains("360", StringComparison.OrdinalIgnoreCase) &&
                    !path.Contains("Qihoo", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (service.Status != ServiceControllerStatus.Stopped)
                {
                    service.Stop();
                    service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                }
                using var process = Process.Start(new ProcessStartInfo("sc.exe",
                    $"delete \"{serviceName}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                process?.WaitForExit(10_000);
                report.Entries.Add(new ReportEntry
                {
                    Target = displayName,
                    Action = "删除 360 服务",
                    Result = "成功",
                    Detail = serviceName
                });
            }
            catch (Exception ex)
            {
                report.Entries.Add(new ReportEntry
                {
                    Target = service.ServiceName,
                    Action = "删除 360 服务",
                    Result = "失败",
                    Detail = ex.Message
                });
            }
            finally
            {
                service.Dispose();
            }
        }
    }

    private static string ReadServicePath(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            return key?.GetValue("ImagePath")?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void Cleanup360Paths(
        IEnumerable<InstalledAppInfo> selected,
        OperationReport report)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in selected)
        {
            if (!string.IsNullOrWhiteSpace(app.InstallLocation))
            {
                roots.Add(app.InstallLocation);
            }
        }

        AddIfInsideProgramFiles(roots, "360");
        AddIfInsideProgramFiles(roots, "Qihoo");
        AddIfInsideProgramData(roots, "360");
        AddIfInsideProgramData(roots, "Qihoo");
        AddIfInsideLocalAppData(roots, "360");
        AddIfInsideLocalAppData(roots, "Qihoo");
        AddMatchingDirectories(roots, Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData));
        AddMatchingDirectories(roots, Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData));
        AddMatchingDirectories(roots, Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile));

        foreach (var root in roots)
        {
            DeleteDirectory(root, report, "清理 360 目录");
        }
    }

    private static void Cleanup360Registry(OperationReport report)
    {
        string[] keyPaths =
        [
            @"SOFTWARE\360",
            @"SOFTWARE\360Safe",
            @"SOFTWARE\360se",
            @"SOFTWARE\Qihoo",
            @"SOFTWARE\WOW6432Node\360",
            @"SOFTWARE\WOW6432Node\Qihoo",
            @"SOFTWARE\Classes\360",
            @"SOFTWARE\Classes\Qihoo"
        ];

        foreach (var path in keyPaths)
        {
            TryDeleteRegistryTree(RegistryHive.LocalMachine, RegistryView.Registry64, path, report);
            TryDeleteRegistryTree(RegistryHive.LocalMachine, RegistryView.Registry32, path, report);
            TryDeleteRegistryTree(RegistryHive.CurrentUser, RegistryView.Registry64, path, report);
            TryDeleteRegistryTree(RegistryHive.CurrentUser, RegistryView.Registry32, path, report);
        }

        CleanupMatchingUninstallKeys(
            RegistryHive.LocalMachine, RegistryView.Registry64, report);
        CleanupMatchingUninstallKeys(
            RegistryHive.LocalMachine, RegistryView.Registry32, report);
        CleanupMatchingUninstallKeys(
            RegistryHive.CurrentUser, RegistryView.Registry64, report);
        CleanupMatchingUninstallKeys(
            RegistryHive.CurrentUser, RegistryView.Registry32, report);
    }

    private static void CleanupMatchingUninstallKeys(
        RegistryHive hive,
        RegistryView view,
        OperationReport report)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", true);
            if (uninstall is null)
            {
                return;
            }

            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                using var subKey = uninstall.OpenSubKey(subKeyName);
                var name = subKey?.GetValue("DisplayName")?.ToString() ?? string.Empty;
                var publisher = subKey?.GetValue("Publisher")?.ToString() ?? string.Empty;
                if (!subKeyName.Contains("360", StringComparison.OrdinalIgnoreCase) &&
                    !subKeyName.Contains("Qihoo", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("360", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("奇虎", StringComparison.OrdinalIgnoreCase) &&
                    !publisher.Contains("360", StringComparison.OrdinalIgnoreCase) &&
                    !publisher.Contains("Qihoo", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                uninstall.DeleteSubKeyTree(subKeyName, false);
                report.Entries.Add(new ReportEntry
                {
                    Target = name.Length > 0 ? name : subKeyName,
                    Action = "清理 360 卸载登记",
                    Result = "成功",
                    Detail = $@"{hive}\{view}"
                });
            }
        }
        catch (Exception ex)
        {
            report.Entries.Add(new ReportEntry
            {
                Target = "360 Uninstall Registry",
                Action = "清理 360 卸载登记",
                Result = "失败",
                Detail = ex.Message
            });
        }
    }

    private static void CleanupResidualData(InstalledAppInfo app, OperationReport report)
    {
        if (!string.IsNullOrWhiteSpace(app.InstallLocation))
        {
            DeleteDirectory(app.InstallLocation, report, "清理安装目录");
        }

        var tokens = Tokenize(app.Name, app.Publisher);
        foreach (var root in CandidateDataRoots())
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                foreach (var directory in Directory.EnumerateDirectories(root))
                {
                    var name = Path.GetFileName(directory);
                    if (!MatchesAppName(name, tokens))
                    {
                        continue;
                    }
                    DeleteDirectory(directory, report, "清理本地数据");
                }
            }
            catch (Exception ex)
            {
                report.Entries.Add(new ReportEntry
                {
                    Target = root,
                    Action = "扫描本地数据",
                    Result = "失败",
                    Detail = ex.Message
                });
            }
        }
    }

    private static IEnumerable<string> CandidateDataRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    }

    private static HashSet<string> Tokenize(params string[] values)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            foreach (var token in value.Split(
                         [' ', '-', '_', '.', '(', ')', '[', ']', '\\', '/'],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Length >= 4 &&
                    token is not ("microsoft" or "windows" or "corporation" or "limited"))
                {
                    tokens.Add(token);
                }
            }
        }
        return tokens;
    }

    private static bool MatchesAppName(string directoryName, HashSet<string> tokens)
    {
        if (tokens.Count == 0)
        {
            return false;
        }
        var matches = tokens.Count(token =>
            directoryName.Contains(token, StringComparison.OrdinalIgnoreCase));
        return matches == tokens.Count || (tokens.Count > 1 && matches >= 2);
    }

    private static void DeleteDirectory(
        string path,
        OperationReport report,
        string action)
    {
        if (!IsSafeDeletionTarget(path))
        {
            report.Entries.Add(new ReportEntry
            {
                Target = path,
                Action = action,
                Result = "保护跳过",
                Detail = "路径为空、位于磁盘根目录，或未通过安全校验"
            });
            return;
        }

        if (!Directory.Exists(path))
        {
            report.Entries.Add(new ReportEntry
            {
                Target = path,
                Action = action,
                Result = "不存在",
                Detail = string.Empty
            });
            return;
        }

        try
        {
            ForceDeleteDirectory(path);
            report.Entries.Add(new ReportEntry
            {
                Target = path,
                Action = action,
                Result = "成功",
                Detail = string.Empty
            });
        }
        catch (Exception ex)
        {
            report.Entries.Add(new ReportEntry
            {
                Target = path,
                Action = action,
                Result = "失败",
                Detail = ex.Message
            });
        }
    }

    private static void ForceDeleteDirectory(string path)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    return;
                }
                Directory.Delete(path, true);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (attempt == 0 && CleanupService.IsAdministrator())
                {
                    RunElevatedPathCommand("takeown.exe", ["/F", path, "/R", "/D", "Y"]);
                    RunElevatedPathCommand("icacls.exe", [path, "/grant", "*S-1-5-32-544:F", "/T", "/C"]);
                }
                Thread.Sleep(500);
            }
        }
        throw lastError ?? new IOException("目录删除失败");
    }

    private static void RunElevatedPathCommand(string fileName, IEnumerable<string> arguments)
    {
        try
        {
            var info = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }
            using var process = Process.Start(info);
            process?.WaitForExit(15_000);
        }
        catch
        {
            // Deletion will retry and report the final error.
        }
    }

    private static bool IsSafeDeletionTarget(string path)
    {
        try
        {
            var full = Path.GetFullPath(path).TrimEnd('\\');
            var root = Path.GetPathRoot(full)?.TrimEnd('\\');
            if (string.IsNullOrWhiteSpace(full) ||
                string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string[] protectedPaths =
            [
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            ];
            return !protectedPaths
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => Path.GetFullPath(item).TrimEnd('\\'))
                .Any(item => string.Equals(full, item, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static void TryDeleteRegistryTree(
        RegistryHive hive,
        RegistryView view,
        string path,
        OperationReport report)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            var parentPath = Path.GetDirectoryName(path)?.Replace('/', '\\');
            var keyName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(parentPath) || string.IsNullOrWhiteSpace(keyName))
            {
                return;
            }

            using var parent = baseKey.OpenSubKey(parentPath, writable: true);
            if (parent?.OpenSubKey(keyName) is null)
            {
                return;
            }

            parent.DeleteSubKeyTree(keyName, throwOnMissingSubKey: false);
            report.Entries.Add(new ReportEntry
            {
                Target = $@"{hive}\{path}",
                Action = "清理 360 注册表",
                Result = "成功",
                Detail = view.ToString()
            });
        }
        catch (Exception ex)
        {
            report.Entries.Add(new ReportEntry
            {
                Target = $@"{hive}\{path}",
                Action = "清理 360 注册表",
                Result = "失败",
                Detail = ex.Message
            });
        }
    }

    private static void AddIfInsideProgramFiles(ISet<string> roots, string folderName)
    {
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 })
        {
            var candidate = Path.Combine(root, folderName);
            if (Directory.Exists(candidate))
            {
                roots.Add(candidate);
            }
        }
    }

    private static void AddIfInsideProgramData(ISet<string> roots, string folderName)
    {
        var candidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), folderName);
        if (Directory.Exists(candidate))
        {
            roots.Add(candidate);
        }
    }

    private static void AddIfInsideLocalAppData(ISet<string> roots, string folderName)
    {
        var candidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), folderName);
        if (Directory.Exists(candidate))
        {
            roots.Add(candidate);
        }
    }

    private static void AddMatchingDirectories(
        ISet<string> roots,
        string parentDirectory)
    {
        if (string.IsNullOrWhiteSpace(parentDirectory) ||
            !Directory.Exists(parentDirectory))
        {
            return;
        }

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(
                         parentDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(directory);
                if (name.Contains("360", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Qihoo", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("奇虎", StringComparison.OrdinalIgnoreCase))
                {
                    roots.Add(directory);
                }
            }
        }
        catch
        {
            // Parent directories can be inaccessible.
        }
    }

    private static ProcessStartInfo ParseCommand(string command)
    {
        var expanded = Environment.ExpandEnvironmentVariables(command.Trim());
        if (expanded.StartsWith('"'))
        {
            var end = expanded.IndexOf('"', 1);
            if (end > 1)
            {
                return new ProcessStartInfo(expanded[1..end], expanded[(end + 1)..].Trim());
            }
        }

        var exeIndex = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex >= 0)
        {
            var end = exeIndex + 4;
            return new ProcessStartInfo(expanded[..end], expanded[end..].Trim());
        }

        var parts = expanded.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return new ProcessStartInfo(parts[0], parts.Length > 1 ? parts[1] : string.Empty);
    }
}
