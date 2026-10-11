using KCI.Core;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace KCI.Views;

public partial class InfoPage : UserControl
{
    private const string DefaultGithubUrl = "https://github.com/jkx120610-commits";
    private const string FeedbackEmail = "2100680885@qq.com";

    public InfoPage()
    {
        InitializeComponent();
        GithubAuthorText.Text = GitHubDisplayName(DefaultGithubUrl);
    }

    public void SetStaticInfo(SystemInfoService info)
    {
        SystemText.Text =
            $"{info.OperatingSystem}\n" +
            $"设备：{info.ComputerSummary}\n" +
            $"主板：{info.BaseBoard}\n" +
            $"BIOS：{info.Bios}\n" +
            $"启动时间：{info.BootTime}";

        var summary = new List<SpecItem>();
        summary.AddRange(info.CpuSpecs.Take(3));
        summary.AddRange(info.Gpus.Take(2).Select(gpu => new SpecItem
        {
            Label = "显卡",
            Value = gpu.Name
        }));
        summary.AddRange(info.MemoryModules.Take(2).Select(module => new SpecItem
        {
            Label = $"内存 {module.Locator}",
            Value = $"{module.Capacity} · {module.Speed} · {module.Manufacturer}"
        }));
        summary.AddRange(info.StorageDevices.Take(2).Select(storage => new SpecItem
        {
            Label = "存储",
            Value = $"{storage.Name} · {storage.Size} · {storage.MediaType}"
        }));
        SummaryItems.ItemsSource = summary;
    }

    private void GithubButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(DefaultGithubUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "打开 GitHub 失败");
        }
    }

    private void EmailButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(
                $"mailto:{FeedbackEmail}?subject=KCI%20意见反馈")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "打开邮件客户端失败");
        }
    }

    private static string GitHubDisplayName(string url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "未配置";
        }

        var segments = uri.AbsolutePath.Trim('/').Split(
            '/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 ? "@" + segments[0] : "未配置";
    }
}
