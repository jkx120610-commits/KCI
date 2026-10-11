using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace KCI.Core;

public static class ReportService
{
    public static string ReportDirectory
    {
        get
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documents, "KCI", "Reports");
        }
    }

    public static OperationReport Save(OperationReport report)
    {
        Directory.CreateDirectory(ReportDirectory);
        var stamp = report.StartedAt.ToString("yyyyMMdd-HHmmss");
        var stem = $"KCI-{Formatting.SafeFileName(report.Operation)}-{stamp}";
        var htmlPath = Path.Combine(ReportDirectory, stem + ".html");
        var jsonPath = Path.Combine(ReportDirectory, stem + ".json");

        var html = new StringBuilder();
        html.AppendLine("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
        html.AppendLine("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        html.AppendLine("<title>KCI 操作报告</title><style>");
        html.AppendLine("body{margin:0;background:#0b0f14;color:#edf4f7;font:14px 'Microsoft YaHei',sans-serif}");
        html.AppendLine(".wrap{max-width:1100px;margin:36px auto;padding:0 24px}.tag{color:#4ea1ff}");
        html.AppendLine("h1{margin:6px 0 4px;font-size:28px}.meta{color:#8da0af;margin-bottom:24px}");
        html.AppendLine("table{width:100%;border-collapse:collapse;background:#141b23;border-radius:8px;overflow:hidden}");
        html.AppendLine("th,td{padding:11px 13px;text-align:left;border-bottom:1px solid #25313d;vertical-align:top}");
        html.AppendLine("th{background:#18212a;color:#a9bac8}tr:last-child td{border-bottom:0}");
        html.AppendLine("code{color:#f2b84b;word-break:break-all}</style></head><body><div class=\"wrap\">");
        html.AppendLine("<div class=\"tag\">KCI · Kerwin</div><h1>操作报告</h1>");
        html.AppendLine($"<div class=\"meta\">{Html(report.Operation)} · {report.StartedAt:yyyy-MM-dd HH:mm:ss} → {report.FinishedAt:HH:mm:ss}<br>{Html(report.Summary)}</div>");
        html.AppendLine("<table><thead><tr><th>对象</th><th>动作</th><th>结果</th><th>详情</th></tr></thead><tbody>");
        foreach (var entry in report.Entries)
        {
            html.AppendLine($"<tr><td>{Html(entry.Target)}</td><td>{Html(entry.Action)}</td><td>{Html(entry.Result)}</td><td>{Html(entry.Detail)}</td></tr>");
        }
        html.AppendLine("</tbody></table></div></body></html>");

        File.WriteAllText(htmlPath, html.ToString(), Encoding.UTF8);
        report.ReportPath = htmlPath;
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        return report;
    }

    public static void Open(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static string Html(string value) =>
        System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
}
