using System.Diagnostics;

namespace KCI.Core;

public sealed class NetworkOptimizationService
{
    public OperationReport Optimize()
    {
        var report = new OperationReport
        {
            Operation = "网络基础优化",
            StartedAt = DateTime.Now
        };
        Run("ipconfig.exe", ["/flushdns"], report, "刷新 DNS 缓存");
        Run("arp.exe", ["-d", "*"], report, "清理 ARP 缓存");
        Run("netsh.exe", ["int", "tcp", "set", "global", "autotuninglevel=normal"],
            report, "恢复 TCP 自动调优");
        Run("netsh.exe", ["int", "ip", "set", "global", "taskoffload=enabled"],
            report, "启用网络任务卸载");
        Run("netsh.exe", ["interface", "ip", "delete", "arpcache"],
            report, "清理接口 ARP 缓存");

        report.FinishedAt = DateTime.Now;
        report.Summary = "已完成基础网络优化；不影响已保存的 Wi-Fi 密码。";
        ReportService.Save(report);
        return report;
    }

    public OperationReport ResetNetworkStack()
    {
        var report = new OperationReport
        {
            Operation = "重置网络协议栈",
            StartedAt = DateTime.Now
        };
        Run("netsh.exe", ["winsock", "reset"], report, "重置 Winsock");
        Run("netsh.exe", ["int", "ip", "reset"], report, "重置 IP 协议栈");
        Run("netsh.exe", ["int", "ipv6", "reset"], report, "重置 IPv6 协议栈");

        report.FinishedAt = DateTime.Now;
        report.Summary = "网络协议栈已重置，需要重启 Windows 后生效。";
        ReportService.Save(report);
        return report;
    }

    private static void Run(
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
            if (process is null || !process.WaitForExit(15_000))
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

            var output = string.Join(
                " ",
                process.StandardOutput.ReadToEnd().Trim(),
                process.StandardError.ReadToEnd().Trim()).Trim();
            report.Entries.Add(new ReportEntry
            {
                Target = fileName,
                Action = action,
                Result = process.ExitCode == 0 ? "成功" : "需检查",
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
}
