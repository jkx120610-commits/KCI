using KCI.Controls;
using KCI.Core;
using System.Windows;
using System.Windows.Controls;

namespace KCI.Views;

public partial class HelpPage : UserControl
{
    private readonly CleanupService _cleanup = new();
    private readonly JunkCleanupService _junkCleanup = new();
    private readonly WindowsUpdateService _windowsUpdate = new();
    private List<InstalledAppInfo> _apps = [];
    private string _lastReportPath = string.Empty;

    public HelpPage()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshWindowsUpdateStatus();
    }

    private async void Remove360Button_Click(object sender, RoutedEventArgs e)
    {
        Remove360Button.IsEnabled = false;
        try
        {
            SetBusy("正在读取 360 软件清单...");
            await EnsureAppsAsync();
            var products = _apps.Where(app => app.Is360Product).ToList();
            if (products.Count == 0)
            {
                SetBusy("未检测到已安装的 360 软件。");
                return;
            }

            var picker = new AppDialog(
                "选择 360 组件",
                "只会处理列表中已勾选的 360 产品及对应受控残留。",
                "警告：卸载会关闭相关进程，并删除受控目录与 360 注册表键。请关闭正在使用 360 的功能。",
                products,
                confirmText: "下一步") { Owner = Window.GetWindow(this) };
            if (picker.ShowDialog() != true)
            {
                SetBusy("已取消 360 卸载。");
                return;
            }

            var selected = picker.SelectedItems.ToList();
            var first = MessageBox.Show(
                $"已选择 {selected.Count} 个组件：\n\n" +
                string.Join("\n", selected.Select(app => "· " + app.Name)) +
                "\n\n继续后 KCI 将调用卸载程序、停止 360 服务并清理残留。",
                "第一次确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (first != MessageBoxResult.Yes)
            {
                SetBusy("已取消 360 卸载。");
                return;
            }

            var second = MessageBox.Show(
                "最后确认：该操作不可由 KCI 自动撤销。确定开始完全清理 360 全家桶？",
                "第二次确认", MessageBoxButton.YesNo, MessageBoxImage.Stop);
            if (second != MessageBoxResult.Yes)
            {
                SetBusy("已取消 360 卸载。");
                return;
            }

            SetBusy("正在卸载并清理 360 组件，请勿关闭 KCI...");
            var report = await Task.Run(() => _cleanup.Uninstall360(selected));
            CompleteReport(report);
        }
        catch (Exception ex)
        {
            SetBusy($"操作失败：{ex.Message}");
        }
        finally
        {
            Remove360Button.IsEnabled = true;
        }
    }

    private async void ReleaseRamButton_Click(object sender, RoutedEventArgs e)
    {
        ReleaseRamButton.IsEnabled = false;
        try
        {
            SetBusy("正在扫描可结束的应用...");
            var processes = await Task.Run(() => _cleanup.GetReleasableProcesses());
            if (processes.Count == 0)
            {
                SetBusy("没有可安全结束的用户应用。");
                return;
            }

            var dialog = new ProcessDialog(processes) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true)
            {
                SetBusy("已取消 RAM 释放。");
                return;
            }

            var selected = dialog.SelectedProcesses.ToList();
            var confirm = MessageBox.Show(
                $"将结束 {selected.Count} 个应用，预计释放 " +
                $"{selected.Sum(item => item.MemoryMb):0.0} MB 内存。\n\n" +
                "未保存的数据可能丢失。确定继续？",
                "确认 RAM 释放", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                SetBusy("已取消 RAM 释放。");
                return;
            }

            SetBusy("正在结束选中应用...");
            var report = await Task.Run(() => _cleanup.ReleaseMemory(selected));
            CompleteReport(report);
        }
        catch (Exception ex)
        {
            SetBusy($"操作失败：{ex.Message}");
        }
        finally
        {
            ReleaseRamButton.IsEnabled = true;
        }
    }

    private async void DeepCleanButton_Click(object sender, RoutedEventArgs e)
    {
        DeepCleanButton.IsEnabled = false;
        try
        {
            SetBusy("正在读取软件、图标与占用空间...");
            await EnsureAppsAsync(force: true);
            var candidates = _apps
                .Where(app => !app.IsSystemApp &&
                              !(app.Name.Equals("KCI", StringComparison.OrdinalIgnoreCase) ||
                                app.Publisher.Contains("Kerwin", StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (candidates.Count == 0)
            {
                SetBusy("没有可清理的非系统软件。");
                return;
            }

            var picker = new AppDialog(
                "SSD / HDD 超级清除",
                "列表已排除 Windows 系统组件和 KCI。占用数据来自卸载登记与安装目录估算。",
                "危险操作：KCI 会调用官方卸载程序，并删除匹配的安装目录、AppData、ProgramData 数据。请先备份重要数据。",
                candidates,
                confirmText: "下一步") { Owner = Window.GetWindow(this) };
            if (picker.ShowDialog() != true)
            {
                SetBusy("已取消软件清除。");
                return;
            }

            var selected = picker.SelectedItems.ToList();
            var first = MessageBox.Show(
                $"已选择 {selected.Count} 个软件：\n\n" +
                string.Join("\n", selected.Take(12).Select(app => "· " + app.Name)) +
                (selected.Count > 12 ? $"\n· 其余 {selected.Count - 12} 项..." : string.Empty) +
                "\n\n官方卸载程序可能弹出交互窗口。准备好后继续。",
                "第一次确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (first != MessageBoxResult.Yes)
            {
                SetBusy("已取消软件清除。");
                return;
            }

            var second = MessageBox.Show(
                "最后确认：删除应用及其本地数据后通常无法恢复。确定执行超级清除？",
                "第二次确认", MessageBoxButton.YesNo, MessageBoxImage.Stop);
            if (second != MessageBoxResult.Yes)
            {
                SetBusy("已取消软件清除。");
                return;
            }

            SetBusy("正在卸载软件并清理受控数据残留...");
            var report = await Task.Run(() => _cleanup.CleanApplications(selected));
            CompleteReport(report);
        }
        catch (Exception ex)
        {
            SetBusy($"操作失败：{ex.Message}");
        }
        finally
        {
            DeepCleanButton.IsEnabled = true;
        }
    }

    private async void CleanJunkButton_Click(object sender, RoutedEventArgs e)
    {
        CleanJunkButton.IsEnabled = false;
        try
        {
            SetBusy("正在扫描下载安装包、软件缓存和系统垃圾...");
            var items = await Task.Run(() => _junkCleanup.Scan());
            if (items.Count == 0)
            {
                SetBusy("没有发现可清理的垃圾数据。");
                return;
            }

            var dialog = new JunkCleanDialog(items) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true)
            {
                SetBusy("已取消垃圾数据清理。");
                return;
            }

            var selected = dialog.SelectedItems.ToList();
            var first = MessageBox.Show(Window.GetWindow(this),
                $"已选择 {selected.Count} 项，预计清理 " +
                $"{Formatting.Bytes(selected.Sum(item => Math.Max(0, item.SizeBytes)))}。\n\n" +
                "缓存和垃圾数据删除后不可恢复；下载目录中的安装包不会被默认选择。继续？",
                "第一次确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (first != MessageBoxResult.Yes)
            {
                SetBusy("已取消垃圾数据清理。");
                return;
            }

            var second = MessageBox.Show(Window.GetWindow(this),
                "最后确认：确定按当前勾选项执行垃圾数据清理？",
                "第二次确认", MessageBoxButton.YesNo, MessageBoxImage.Stop);
            if (second != MessageBoxResult.Yes)
            {
                SetBusy("已取消垃圾数据清理。");
                return;
            }

            SetBusy("正在清理选中项目...");
            var report = await Task.Run(() => _junkCleanup.Clean(selected));
            CompleteReport(report);
        }
        catch (Exception ex)
        {
            SetBusy($"垃圾数据清理失败：{ex.Message}");
        }
        finally
        {
            CleanJunkButton.IsEnabled = true;
        }
    }

    private async Task EnsureAppsAsync(bool force = false)
    {
        if (_apps.Count > 0 && !force)
        {
            return;
        }
        _apps = await Task.Run(() => InstalledAppService.GetInstalledApps());
    }

    private void CompleteReport(OperationReport report)
    {
        _lastReportPath = report.ReportPath;
        SetBusy($"{report.Summary}\n报告：{report.ReportPath}");
        OpenReportButton.IsEnabled = File.Exists(_lastReportPath);
    }

    private void SetBusy(string message)
    {
        StatusText.Text = message;
        LastReportText.Text = string.IsNullOrWhiteSpace(_lastReportPath)
            ? string.Empty
            : $"最近报告：{_lastReportPath}";
    }

    private void OpenReportButton_Click(object sender, RoutedEventArgs e)
    {
        ReportService.Open(_lastReportPath);
    }

    private void RefreshWindowsUpdateStatus()
    {
        UpdateStatusText.Text = _windowsUpdate.GetStatus().Text;
    }

    private async void PauseUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CleanupService.IsAdministrator())
        {
            MessageBox.Show(Window.GetWindow(this),
                "暂停 Windows 更新需要管理员权限。", "KCI",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var duration = UpdateSixMonths.IsChecked == true
            ? WindowsUpdateBlockDuration.SixMonths
            : UpdateOneYear.IsChecked == true
                ? WindowsUpdateBlockDuration.OneYear
                : WindowsUpdateBlockDuration.OneMonth;
        var durationText = duration switch
        {
            WindowsUpdateBlockDuration.SixMonths => "半年",
            WindowsUpdateBlockDuration.OneYear => "1 年",
            _ => "1 个月"
        };

        var confirm = MessageBox.Show(Window.GetWindow(this),
            $"将暂停 Windows 更新 {durationText}。\n\n" +
            "这会禁用更新服务、计划任务和自动更新策略，安全更新也将停止。" +
            "到期后由 KCI 自动恢复。确定继续？",
            "Windows 停止更新", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        PauseUpdatesButton.IsEnabled = false;
        RestoreUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = "正在配置 Windows 更新策略和恢复任务...";
        try
        {
            var report = await Task.Run(() => _windowsUpdate.PauseWindowsUpdate(duration));
            CompleteReport(report);
            RefreshWindowsUpdateStatus();
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"配置失败：{ex.Message}";
        }
        finally
        {
            PauseUpdatesButton.IsEnabled = true;
            RestoreUpdatesButton.IsEnabled = true;
        }
    }

    private async void RestoreUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CleanupService.IsAdministrator())
        {
            MessageBox.Show(Window.GetWindow(this),
                "恢复 Windows 更新需要管理员权限。", "KCI",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(Window.GetWindow(this),
            "恢复 Windows 更新服务、计划任务和默认策略？", "KCI",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        PauseUpdatesButton.IsEnabled = false;
        RestoreUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = "正在恢复 Windows 更新...";
        try
        {
            var report = await Task.Run(() => _windowsUpdate.RestoreWindowsUpdate());
            CompleteReport(report);
            RefreshWindowsUpdateStatus();
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = $"恢复失败：{ex.Message}";
        }
        finally
        {
            PauseUpdatesButton.IsEnabled = true;
            RestoreUpdatesButton.IsEnabled = true;
        }
    }
}
