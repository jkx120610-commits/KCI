namespace KCI.Core;

public sealed class JunkCleanupService
{
    private static readonly string[] InstallerExtensions =
    [
        ".exe", ".msi", ".msix", ".appx", ".zip", ".rar", ".7z", ".iso"
    ];

    public IReadOnlyList<JunkItem> Scan()
    {
        var items = new List<JunkItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddDownloadedInstallers(items, seen);
        AddCacheDirectories(items, seen);
        AddJunkFilesAndDirectories(items, seen);
        AddRecycleBin(items);

        return items
            .OrderByDescending(item => item.IsRecommended)
            .ThenByDescending(item => item.SizeBytes)
            .ThenBy(item => item.Category)
            .ToList();
    }

    public OperationReport Clean(IEnumerable<JunkItem> selectedItems)
    {
        var report = new OperationReport
        {
            Operation = "垃圾数据清理",
            StartedAt = DateTime.Now
        };
        var selected = selectedItems.ToList();
        foreach (var item in selected)
        {
            switch (item.Kind)
            {
                case JunkItemKind.File:
                    DeleteFile(item, report);
                    break;
                case JunkItemKind.DirectoryContents:
                    DeleteDirectoryContents(item, report);
                    break;
                case JunkItemKind.RecycleBin:
                    EmptyRecycleBin(item, report);
                    break;
            }
        }

        report.FinishedAt = DateTime.Now;
        var selectedBytes = selected.Sum(item => Math.Max(0, item.SizeBytes));
        var success = report.Entries.Count(entry => entry.Result == "成功");
        report.Summary =
            $"选择 {selected.Count} 项，成功清理 {success} 项，预计释放空间 {Formatting.Bytes(selectedBytes)}。";
        ReportService.Save(report);
        return report;
    }

    private static void AddDownloadedInstallers(
        ICollection<JunkItem> items,
        ISet<string> seen)
    {
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (!Directory.Exists(downloads))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(
                         downloads, "*", SearchOption.TopDirectoryOnly))
            {
                var extension = Path.GetExtension(file);
                if (!InstallerExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
                    string.Equals(file, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                long size;
                try
                {
                    size = new FileInfo(file).Length;
                }
                catch
                {
                    continue;
                }
                if (size < 1024 * 1024 || !seen.Add(file))
                {
                    continue;
                }

                items.Add(new JunkItem
                {
                    Category = "下载未安装文件",
                    Name = Path.GetFileName(file),
                    Path = file,
                    Glyph = "\uE896",
                    RiskText = "请确认",
                    SizeBytes = size,
                    IsRecommended = false,
                    Kind = JunkItemKind.File,
                    AllowedRoot = downloads
                });
            }
        }
        catch
        {
            // Downloads can be unavailable or redirected.
        }
    }

    private static void AddCacheDirectories(
        ICollection<JunkItem> items,
        ISet<string> seen)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        AddDirectory(items, seen, "软件缓存", "用户临时文件",
            Path.Combine(local, "Temp"), "\uE74D", true);
        AddDirectory(items, seen, "软件缓存", "浏览器网络缓存",
            Path.Combine(local, "Microsoft", "Windows", "INetCache"), "\uE774", true);
        AddDirectory(items, seen, "软件缓存", "Chrome 缓存",
            Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Cache"), "\uE774", true);
        AddDirectory(items, seen, "软件缓存", "Chrome GPU 缓存",
            Path.Combine(local, "Google", "Chrome", "User Data", "Default", "GPUCache"), "\uE774", true);
        AddDirectory(items, seen, "软件缓存", "Edge 缓存",
            Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Cache"), "\uE774", true);
        AddDirectory(items, seen, "软件缓存", "Edge GPU 缓存",
            Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "GPUCache"), "\uE774", true);
        AddDirectory(items, seen, "开发缓存", "pip 下载缓存",
            Path.Combine(local, "pip", "Cache"), "\uE943", true);
        AddDirectory(items, seen, "开发缓存", "npm 缓存",
            Path.Combine(local, "npm-cache"), "\uE943", true);
        AddDirectory(items, seen, "开发缓存", "NuGet HTTP 缓存",
            Path.Combine(local, "NuGet", "v3-cache"), "\uE943", true);
        AddDirectory(items, seen, "开发缓存", "NuGet 插件缓存",
            Path.Combine(local, "NuGet", "plugins-cache"), "\uE943", true);
        AddDirectory(items, seen, "开发缓存", "Gradle 缓存",
            Path.Combine(user, ".gradle", "caches"), "\uE943", true);
        AddDirectory(items, seen, "开发缓存", "JetBrains 缓存",
            Path.Combine(local, "JetBrains"), "\uE943", true);
        AddDirectory(items, seen, "开发缓存", "Visual Studio 组件缓存",
            Path.Combine(local, "Microsoft", "VisualStudio", "ComponentModelCache"), "\uE943", true);
        AddDirectory(items, seen, "软件缓存", "桌面应用漫游缓存",
            Path.Combine(appData, "Code", "Cache"), "\uE774", true);
        AddDirectory(items, seen, "软件缓存", "应用崩溃转储",
            Path.Combine(local, "CrashDumps"), "\uE7BA", true);
        AddDirectory(items, seen, "系统垃圾", "Windows 错误报告",
            Path.Combine(programData, "Microsoft", "Windows", "WER"), "\uE7BA", true);
    }

    private static void AddJunkFilesAndDirectories(
        ICollection<JunkItem> items,
        ISet<string> seen)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        AddDirectory(items, seen, "系统垃圾", "Windows 临时文件",
            Path.Combine(windows, "Temp"), "\uE7BA", true);
        AddDirectory(items, seen, "系统垃圾", "Windows 更新下载缓存",
            Path.Combine(windows, "SoftwareDistribution", "Download"), "\uE895", false);

        var thumbnailCache = Path.Combine(
            local, "Microsoft", "Windows", "Explorer");
        if (!Directory.Exists(thumbnailCache))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(
                         thumbnailCache, "thumbcache_*.db", SearchOption.TopDirectoryOnly))
            {
                if (!seen.Add(file))
                {
                    continue;
                }
                try
                {
                    items.Add(new JunkItem
                    {
                        Category = "系统垃圾",
                        Name = Path.GetFileName(file),
                        Path = file,
                        Glyph = "\uEB9F",
                        RiskText = "可清理",
                        SizeBytes = new FileInfo(file).Length,
                        IsRecommended = true,
                        Kind = JunkItemKind.File,
                        AllowedRoot = thumbnailCache
                    });
                }
                catch
                {
                    // Thumbnail cache can be locked while Explorer is using it.
                }
            }
        }
        catch
        {
            // Explorer cache directory is optional.
        }
    }

    private static void AddDirectory(
        ICollection<JunkItem> items,
        ISet<string> seen,
        string category,
        string name,
        string path,
        string glyph,
        bool recommended)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) || !seen.Add(path))
        {
            return;
        }

        var size = GetDirectorySize(path);
        if (size <= 0)
        {
            return;
        }

        items.Add(new JunkItem
        {
            Category = category,
            Name = name,
            Path = path,
            Glyph = glyph,
            RiskText = recommended ? "可清理" : "建议确认",
            SizeBytes = size,
            IsRecommended = recommended,
            Kind = JunkItemKind.DirectoryContents,
            AllowedRoot = path
        });
    }

    private static void AddRecycleBin(ICollection<JunkItem> items)
    {
        try
        {
            var info = new NativeMethods.ShellRecycleBinInfo
            {
                Size = System.Runtime.InteropServices.Marshal
                    .SizeOf<NativeMethods.ShellRecycleBinInfo>()
            };
            if (NativeMethods.SHQueryRecycleBin(null, ref info) != 0 ||
                info.ItemCount <= 0 ||
                info.TotalSize <= 0)
            {
                return;
            }

            items.Add(new JunkItem
            {
                Category = "系统垃圾",
                Name = "回收站",
                Path = "所有驱动器的回收站",
                Glyph = "\uE74D",
                RiskText = "清空回收站",
                SizeBytes = info.TotalSize,
                IsRecommended = true,
                Kind = JunkItemKind.RecycleBin,
                ItemCount = (int)Math.Min(int.MaxValue, info.ItemCount)
            });
        }
        catch
        {
            // Recycle Bin may be unavailable in restricted sessions.
        }
    }

    private static long GetDirectorySize(string root)
    {
        long size = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(
                             directory, "*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        size += new FileInfo(file).Length;
                    }
                    catch
                    {
                        // Files can disappear or be locked during scanning.
                    }
                }

                foreach (var child in Directory.EnumerateDirectories(
                             directory, "*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                        {
                            pending.Push(child);
                        }
                    }
                    catch
                    {
                        // Ignore inaccessible child directories.
                    }
                }
            }
            catch
            {
                // Ignore inaccessible directories.
            }
        }
        return size;
    }

    private static void DeleteFile(JunkItem item, OperationReport report)
    {
        if (!IsWithinAllowedRoot(item.Path, item.AllowedRoot))
        {
            AddSkipped(report, item, "路径不在允许的下载或缓存目录中");
            return;
        }

        try
        {
            if (File.Exists(item.Path))
            {
                File.SetAttributes(item.Path, FileAttributes.Normal);
                File.Delete(item.Path);
                AddSuccess(report, item);
            }
            else
            {
                AddMissing(report, item);
            }
        }
        catch (Exception ex)
        {
            AddFailure(report, item, ex.Message);
        }
    }

    private static void DeleteDirectoryContents(JunkItem item, OperationReport report)
    {
        if (!Directory.Exists(item.Path) ||
            !IsSameOrChildPath(item.Path, item.AllowedRoot))
        {
            AddSkipped(report, item, "缓存目录不存在或安全校验失败");
            return;
        }

        var failures = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(
                         item.Path, "*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    failures.Add($"{Path.GetFileName(file)}: {ex.Message}");
                }
            }

            foreach (var directory in Directory.EnumerateDirectories(
                         item.Path, "*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    Directory.Delete(directory, true);
                }
                catch (Exception ex)
                {
                    failures.Add($"{Path.GetFileName(directory)}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            failures.Add(ex.Message);
        }

        if (failures.Count == 0)
        {
            AddSuccess(report, item);
        }
        else
        {
            report.Entries.Add(new ReportEntry
            {
                Target = item.Name,
                Action = "清理缓存数据",
                Result = "部分完成",
                Detail = Formatting.Compact(string.Join("；", failures), 220)
            });
        }
    }

    private static void EmptyRecycleBin(JunkItem item, OperationReport report)
    {
        try
        {
            var result = NativeMethods.SHEmptyRecycleBin(
                IntPtr.Zero,
                null,
                NativeMethods.SherbNoConfirmation |
                NativeMethods.SherbNoProgressUi |
                NativeMethods.SherbNoSound);
            if (result == 0)
            {
                AddSuccess(report, item);
            }
            else
            {
                AddFailure(report, item, $"Shell 返回代码 {result}");
            }
        }
        catch (Exception ex)
        {
            AddFailure(report, item, ex.Message);
        }
    }

    private static bool IsWithinAllowedRoot(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
        {
            return false;
        }
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        return fullPath.StartsWith(
            fullRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameOrChildPath(string path, string root)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(
                   fullRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void AddSuccess(OperationReport report, JunkItem item) =>
        report.Entries.Add(new ReportEntry
        {
            Target = item.Name,
            Action = item.Category,
            Result = "成功",
            Detail = item.PathText
        });

    private static void AddFailure(OperationReport report, JunkItem item, string detail) =>
        report.Entries.Add(new ReportEntry
        {
            Target = item.Name,
            Action = item.Category,
            Result = "失败",
            Detail = detail
        });

    private static void AddSkipped(OperationReport report, JunkItem item, string detail) =>
        report.Entries.Add(new ReportEntry
        {
            Target = item.Name,
            Action = item.Category,
            Result = "保护跳过",
            Detail = detail
        });

    private static void AddMissing(OperationReport report, JunkItem item) =>
        report.Entries.Add(new ReportEntry
        {
            Target = item.Name,
            Action = item.Category,
            Result = "不存在",
            Detail = item.PathText
        });
}
