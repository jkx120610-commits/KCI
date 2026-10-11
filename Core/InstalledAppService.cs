using Microsoft.Win32;
using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace KCI.Core;

public static class InstalledAppService
{
    private static readonly Dictionary<string, long> DirectorySizeCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    private static readonly string[] SystemKeywords =
    [
        "microsoft visual c++", "windows sdk", "windows software development kit",
        "microsoft .net", ".net runtime", "windows app runtime", "microsoft edge",
        "microsoft edge update", "microsoft edge webview", "windows update",
        "directx", "microsoft visual studio installer", "microsoft vclibs"
    ];

    public static List<InstalledAppInfo> GetInstalledApps(bool includeIcons = true)
    {
        var apps = new Dictionary<string, InstalledAppInfo>(StringComparer.OrdinalIgnoreCase);
        ReadRegistry(RegistryHive.LocalMachine, RegistryView.Registry64, apps);
        ReadRegistry(RegistryHive.LocalMachine, RegistryView.Registry32, apps);
        ReadRegistry(RegistryHive.CurrentUser, RegistryView.Registry64, apps);
        ReadRegistry(RegistryHive.CurrentUser, RegistryView.Registry32, apps);

        var result = apps.Values
            .Where(app => !string.IsNullOrWhiteSpace(app.Name))
            .OrderByDescending(app => app.Is360Product)
            .ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        ResolveSizes(result);
        if (includeIcons)
        {
            foreach (var app in result)
            {
                app.Icon = TryGetIcon(app);
            }
        }
        return result;
    }

    private static void ReadRegistry(
        RegistryHive hive,
        RegistryView view,
        IDictionary<string, InstalledAppInfo> apps)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            foreach (var path in new[]
                     {
                         @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                         @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                     })
            {
                using var uninstall = baseKey.OpenSubKey(path);
                if (uninstall is null)
                {
                    continue;
                }

                foreach (var subKeyName in uninstall.GetSubKeyNames())
                {
                    using var subKey = uninstall.OpenSubKey(subKeyName);
                    if (subKey is null)
                    {
                        continue;
                    }

                    var name = Clean(subKey.GetValue("DisplayName")?.ToString());
                    if (string.IsNullOrWhiteSpace(name) ||
                        subKey.GetValue("SystemComponent")?.ToString() == "1" ||
                        subKey.GetValue("ReleaseType")?.ToString() is "Update" or "Hotfix")
                    {
                        continue;
                    }

                    var publisher = Clean(subKey.GetValue("Publisher")?.ToString());
                    var displayIcon = Clean(subKey.GetValue("DisplayIcon")?.ToString());
                    var uninstallString = Clean(subKey.GetValue("UninstallString")?.ToString());
                    var quietUninstallString = Clean(subKey.GetValue("QuietUninstallString")?.ToString());
                    var installLocation = ResolveInstallLocation(
                        subKey.GetValue("InstallLocation")?.ToString(),
                        displayIcon,
                        quietUninstallString,
                        uninstallString);
                    var is360 = name.Contains("360", StringComparison.OrdinalIgnoreCase) ||
                                publisher.Contains("360", StringComparison.OrdinalIgnoreCase) ||
                                subKeyName.Contains("360", StringComparison.OrdinalIgnoreCase);
                    var app = new InstalledAppInfo
                    {
                        Name = name,
                        Publisher = string.IsNullOrWhiteSpace(publisher) ? "未知发布者" : publisher,
                        Version = Clean(subKey.GetValue("DisplayVersion")?.ToString()),
                        InstallLocation = installLocation,
                        UninstallString = uninstallString,
                        QuietUninstallString = quietUninstallString,
                        DisplayIcon = displayIcon,
                        RegistryKey = $@"{hive}\{path}\{subKeyName}",
                        RegistryView = view == RegistryView.Registry64 ? "64-bit" : "32-bit",
                        ReportedSizeBytes = ToLong(subKey.GetValue("EstimatedSize")) * 1024L,
                        EstimatedSizeBytes = 0,
                        Is360Product = is360,
                        IsSystemApp = IsSystemApp(name, publisher)
                    };

                    var identity = $"{app.Name}|{app.Version}|{app.Publisher}";
                    apps.TryAdd(identity, app);
                }
            }
        }
        catch
        {
            // A broken registry hive should not prevent the remaining inventory from loading.
        }
    }

    private static bool IsSystemApp(string name, string publisher)
    {
        var combined = $"{name} {publisher}".ToLowerInvariant();
        return SystemKeywords.Any(keyword => combined.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
               name.StartsWith("Windows ", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Microsoft Windows", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Update for", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Hotfix for", StringComparison.OrdinalIgnoreCase);
    }

    private static System.Windows.Media.ImageSource? TryGetIcon(InstalledAppInfo app)
    {
        var candidate = ResolveIconCandidate(app.DisplayIcon);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = ResolveIconCandidate(app.QuietUninstallString);
        }
        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = ResolveIconCandidate(app.UninstallString);
        }
        if (string.IsNullOrWhiteSpace(candidate) &&
            !string.IsNullOrWhiteSpace(app.InstallLocation) &&
            Directory.Exists(app.InstallLocation))
        {
            candidate = FindLikelyExecutable(app.InstallLocation, app.Name);
        }
        if (string.IsNullOrWhiteSpace(candidate) ||
            !File.Exists(candidate))
        {
            return null;
        }

        try
        {
            using var icon = Path.GetExtension(candidate)
                .Equals(".ico", StringComparison.OrdinalIgnoreCase)
                ? new Icon(candidate, 24, 24)
                : Icon.ExtractAssociatedIcon(candidate);
            if (icon is null)
            {
                return null;
            }

            var bitmap = icon.ToBitmap();
            var handle = bitmap.GetHbitmap();
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    handle, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(24, 24));
                source.Freeze();
                return source;
            }
            finally
            {
                NativeMethods.DeleteObject(handle);
                bitmap.Dispose();
            }
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractExecutablePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        var expanded = Environment.ExpandEnvironmentVariables(command.Trim());
        var comma = expanded.LastIndexOf(',');
        if (comma > 0 && int.TryParse(expanded[(comma + 1)..].Trim(), out _))
        {
            expanded = expanded[..comma];
        }
        if (expanded.StartsWith('"'))
        {
            var end = expanded.IndexOf('"', 1);
            return end > 1 ? expanded[1..end] : string.Empty;
        }

        var exeIndex = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exeIndex >= 0 ? expanded[..(exeIndex + 4)].Trim() : expanded.Split(' ')[0];
    }

    private static string ResolveIconCandidate(string command)
    {
        var path = ExtractExecutablePath(command);
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path)
            ? path
            : string.Empty;
    }

    private static string FindLikelyExecutable(string installLocation, string appName)
    {
        try
        {
            var tokens = appName.Split(
                [' ', '-', '_', '.', '(', ')', '[', ']'],
                StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.Length >= 4)
                .ToArray();
            var executables = Directory.EnumerateFiles(
                installLocation, "*.exe", SearchOption.TopDirectoryOnly).ToList();
            return executables.FirstOrDefault(path =>
                       tokens.Any(token => Path.GetFileName(path)
                           .Contains(token, StringComparison.OrdinalIgnoreCase)))
                   ?? executables.FirstOrDefault(path =>
                       !Path.GetFileName(path).Contains("unins", StringComparison.OrdinalIgnoreCase))
                   ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;

    private static string NormalizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }
        try
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim().Trim('"')));
        }
        catch
        {
            return value.Trim();
        }
    }

    private static string ResolveInstallLocation(
        string? installLocation,
        string displayIcon,
        string quietUninstallString,
        string uninstallString)
    {
        var normalized = NormalizePath(installLocation);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        foreach (var command in new[] { displayIcon, quietUninstallString, uninstallString })
        {
            var executable = ExtractExecutablePath(command);
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(executable);
            if (!string.IsNullOrWhiteSpace(directory) &&
                !directory.StartsWith(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(directory);
            }
        }
        return string.Empty;
    }

    private static void ResolveSizes(IEnumerable<InstalledAppInfo> apps)
    {
        foreach (var app in apps)
        {
            if (app.ReportedSizeBytes > 0)
            {
                app.EstimatedSizeBytes = app.ReportedSizeBytes;
                app.SizeSource = "系统登记";
                continue;
            }

            if (!string.IsNullOrWhiteSpace(app.InstallLocation))
            {
                app.EstimatedSizeBytes = MeasureApplicationFootprint(
                    app.InstallLocation, app.Name);
                app.SizeSource = app.EstimatedSizeBytes > 0 ? "目录估算" : "未知";
                continue;
            }

            app.SizeSource = "未知";
        }
    }

    public static long GetMeasuredSize(InstalledAppInfo app)
    {
        if (app.EstimatedSizeBytes > 0)
        {
            return app.EstimatedSizeBytes;
        }
        if (string.IsNullOrWhiteSpace(app.InstallLocation))
        {
            return 0;
        }

        var measured = MeasureApplicationFootprint(app.InstallLocation, app.Name);
        app.EstimatedSizeBytes = measured;
        app.SizeSource = measured > 0 ? "目录估算" : "未知";
        return measured;
    }

    public static void ClearDirectorySizeCache()
    {
        lock (CacheLock)
        {
            DirectorySizeCache.Clear();
        }
    }

    private static long ToLong(object? value)
    {
        return long.TryParse(value?.ToString(), out var parsed) ? parsed : 0;
    }

    private static long TryDirectorySize(string path)
    {
        return TryDirectorySize(path, Array.Empty<string>());
    }

    private static long MeasureApplicationFootprint(string installLocation, string appName)
    {
        var excluded = ExcludedTopLevelDirectories(installLocation, appName);
        return TryDirectorySize(installLocation, excluded);
    }

    private static long TryDirectorySize(string path, IReadOnlyCollection<string> excludedDirectories)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return 0;
            }

            var cacheKey = path + "|" + string.Join("|", excludedDirectories.OrderBy(value => value));
            lock (CacheLock)
            {
                if (DirectorySizeCache.TryGetValue(cacheKey, out var cached))
                {
                    return cached;
                }
            }

            long size = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    size += new FileInfo(file).Length;
                }
                catch
                {
                    // Ignore files that disappear or deny access during measurement.
                }
            }

            foreach (var directory in Directory.EnumerateDirectories(
                         path, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(directory);
                if (excludedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (IsReparsePoint(directory))
                {
                    continue;
                }
                size += TryDirectorySize(directory, Array.Empty<string>(), useCache: false);
            }

            lock (CacheLock)
            {
                DirectorySizeCache[cacheKey] = size;
            }
            return size;
        }
        catch
        {
            return 0;
        }
    }

    private static long TryDirectorySize(
        string path,
        IReadOnlyCollection<string> excludedDirectories,
        bool useCache)
    {
        try
        {
            long size = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    size += new FileInfo(file).Length;
                }
                catch
                {
                    // Ignore inaccessible files.
                }
            }
            foreach (var directory in Directory.EnumerateDirectories(
                         path, "*", SearchOption.TopDirectoryOnly))
            {
                if (IsReparsePoint(directory))
                {
                    continue;
                }
                size += TryDirectorySize(directory, Array.Empty<string>(), false);
            }
            return size;
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return true;
        }
    }

    private static IReadOnlyCollection<string> ExcludedTopLevelDirectories(
        string installLocation,
        string appName)
    {
        var leaf = Path.GetFileName(installLocation.TrimEnd('\\'));
        if (leaf.Equals("Steam", StringComparison.OrdinalIgnoreCase))
        {
            return ["steamapps", "userdata", "dumps", "logs"];
        }
        if (leaf.Equals("WeGameApps", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("WeGame", StringComparison.OrdinalIgnoreCase))
        {
            return ["rail_apps", "rail_downloads", "downloads"];
        }
        if (leaf.Equals("Epic Games", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("Riot Games", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("Battle.net", StringComparison.OrdinalIgnoreCase))
        {
            return Directory.EnumerateDirectories(installLocation, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .ToList();
        }
        return Array.Empty<string>();
    }
}
