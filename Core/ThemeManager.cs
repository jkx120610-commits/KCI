using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using System.Text.Json;

namespace KCI.Core;

public static class ThemeManager
{
    private static bool _initialized;
    private static bool _isLight;
    private static bool _followSystem;
    private static bool _hasSavedPreference;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KCI", "theme.json");

    public static event Action? ThemeChanged;

    public static bool IsLight => _isLight;
    public static bool FollowSystem => _followSystem;
    public static bool HasSavedPreference => _hasSavedPreference;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        LoadPreference();
        if (_hasSavedPreference)
        {
            ApplyTheme(_followSystem ? IsWindowsLightTheme() : true);
        }
        else
        {
            _followSystem = false;
            ApplyTheme(true);
        }
        try
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
        catch
        {
            // Theme changes are optional in restricted sessions.
        }
    }

    public static void Dispose()
    {
        try
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }
        catch
        {
            // Nothing to release.
        }
    }

    public static void SetFollowSystem(bool followSystem)
    {
        _followSystem = followSystem;
        _hasSavedPreference = true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new
            {
                followSystem
            }));
        }
        catch
        {
            // A failed preference write still applies for this session.
        }

        ApplyTheme(followSystem ? IsWindowsLightTheme() : true);
    }

    public static void ApplyTheme(bool light)
    {
        if (Application.Current is null)
        {
            return;
        }

        if (SystemParameters.HighContrast)
        {
            ApplyHighContrastTheme();
            return;
        }

        _isLight = light;
        var palette = light ? LightPalette : DarkPalette;
        foreach (var pair in palette)
        {
            Application.Current.Resources[pair.Key] = new SolidColorBrush(pair.Value);
        }

        ThemeChanged?.Invoke();
    }

    private static void ApplyHighContrastTheme()
    {
        _isLight = false;
        var palette = new Dictionary<string, Color>
        {
            ["WindowBrush"] = SystemColors.WindowColor,
            ["SidebarBrush"] = SystemColors.ControlColor,
            ["CardBrush"] = SystemColors.WindowColor,
            ["CardHoverBrush"] = SystemColors.ControlColor,
            ["BorderBrushSoft"] = SystemColors.ControlDarkColor,
            ["TextPrimaryBrush"] = SystemColors.WindowTextColor,
            ["TextSecondaryBrush"] = SystemColors.GrayTextColor,
            ["TextMutedBrush"] = SystemColors.GrayTextColor,
            ["AccentBrush"] = SystemColors.HighlightColor,
            ["AccentTextBrush"] = SystemColors.HighlightTextColor,
            ["WarmBrush"] = SystemColors.HighlightColor,
            ["DangerBrush"] = SystemColors.HighlightColor,
            ["HoverBrush"] = SystemColors.ControlColor,
            ["PressedBrush"] = SystemColors.ControlDarkColor,
            ["SecondaryButtonBrush"] = SystemColors.ControlColor,
            ["SecondaryBorderBrush"] = SystemColors.ControlDarkColor,
            ["ElementHoverBrush"] = SystemColors.ControlColor,
            ["ElementPressedBrush"] = SystemColors.ControlDarkColor,
            ["InputBrush"] = SystemColors.WindowColor,
            ["InputBorderBrush"] = SystemColors.ControlDarkColor,
            ["PanelInsetBrush"] = SystemColors.WindowColor,
            ["BadgeBackgroundBrush"] = SystemColors.ControlColor,
            ["BadgeBorderBrush"] = SystemColors.ControlDarkColor,
            ["GridLineBrush"] = SystemColors.ControlDarkColor,
            ["HeaderBrush"] = SystemColors.ControlColor,
            ["HeaderBorderBrush"] = SystemColors.ControlDarkColor,
            ["RowHoverBrush"] = SystemColors.ControlColor,
            ["ScrollThumbBrush"] = SystemColors.ControlDarkColor,
            ["ScrollThumbHoverBrush"] = SystemColors.HighlightColor,
            ["ScrollTrackBrush"] = SystemColors.WindowColor,
            ["ScrollTrackHoverBrush"] = SystemColors.ControlColor,
            ["ToolTipBrush"] = SystemColors.InfoColor,
            ["ProgressTrackBrush"] = SystemColors.ControlDarkColor,
            ["WindowHeaderBrush"] = SystemColors.WindowColor,
            ["LoadingOverlayBrush"] = SystemColors.WindowColor,
            ["WarningSurfaceBrush"] = SystemColors.InfoColor,
            ["WarningBorderBrush"] = SystemColors.ControlDarkColor,
            ["WarningTextBrush"] = SystemColors.InfoTextColor,
            ["DangerSurfaceBrush"] = SystemColors.ControlColor,
            ["DangerBorderBrush"] = SystemColors.ControlDarkColor,
            ["DangerTextBrush"] = SystemColors.ControlTextColor,
            ["CloseHoverBrush"] = SystemColors.HighlightColor,
            ["CloseTextBrush"] = SystemColors.HighlightTextColor,
            ["RingTrackBrush"] = SystemColors.ControlDarkColor,
            ["ChartGridBrush"] = SystemColors.ControlDarkColor,
            ["ChartTextBrush"] = SystemColors.WindowTextColor,
            ["BlueAccentBrush"] = SystemColors.HighlightColor,
            ["BlueSurfaceBrush"] = SystemColors.ControlColor
        };
        foreach (var pair in palette)
        {
            Application.Current.Resources[pair.Key] = new SolidColorBrush(pair.Value);
        }
        ThemeChanged?.Invoke();
    }

    private static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (Application.Current is null)
        {
            return;
        }

        if (_followSystem)
        {
            Application.Current.Dispatcher.BeginInvoke(() => ApplyTheme(IsWindowsLightTheme()));
        }
    }

    private static void LoadPreference()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            if (document.RootElement.TryGetProperty("followSystem", out var follow))
            {
                _followSystem = follow.GetBoolean();
                _hasSavedPreference = true;
            }
        }
        catch
        {
            _hasSavedPreference = false;
        }
    }

    private static bool IsWindowsLightTheme()
    {
        try
        {
            var overrideValue = Environment.GetEnvironmentVariable("KCI_THEME");
            if (string.Equals(overrideValue, "light", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (string.Equals(overrideValue, "dark", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 0);
            return value is int intValue && intValue != 0;
        }
        catch
        {
            return false;
        }
    }

    private static readonly Dictionary<string, Color> DarkPalette = new()
    {
        ["WindowBrush"] = Color.FromRgb(11, 15, 20),
        ["SidebarBrush"] = Color.FromRgb(16, 22, 29),
        ["CardBrush"] = Color.FromRgb(20, 27, 35),
        ["CardHoverBrush"] = Color.FromRgb(23, 33, 43),
        ["BorderBrushSoft"] = Color.FromRgb(37, 49, 61),
        ["TextPrimaryBrush"] = Color.FromRgb(242, 246, 248),
        ["TextSecondaryBrush"] = Color.FromRgb(148, 164, 179),
        ["TextMutedBrush"] = Color.FromRgb(101, 118, 135),
        ["AccentBrush"] = Color.FromRgb(78, 161, 255),
        ["AccentTextBrush"] = Color.FromRgb(7, 20, 38),
        ["WarmBrush"] = Color.FromRgb(242, 184, 75),
        ["DangerBrush"] = Color.FromRgb(240, 97, 97),
        ["HoverBrush"] = Color.FromRgb(25, 35, 45),
        ["PressedBrush"] = Color.FromRgb(32, 45, 57),
        ["SecondaryButtonBrush"] = Color.FromRgb(29, 40, 51),
        ["SecondaryBorderBrush"] = Color.FromRgb(51, 67, 82),
        ["ElementHoverBrush"] = Color.FromRgb(38, 52, 66),
        ["ElementPressedBrush"] = Color.FromRgb(49, 67, 82),
        ["InputBrush"] = Color.FromRgb(24, 35, 45),
        ["InputBorderBrush"] = Color.FromRgb(58, 74, 89),
        ["PanelInsetBrush"] = Color.FromRgb(16, 23, 30),
        ["BadgeBackgroundBrush"] = Color.FromRgb(22, 48, 74),
        ["BadgeBorderBrush"] = Color.FromRgb(61, 126, 191),
        ["GridLineBrush"] = Color.FromRgb(32, 43, 53),
        ["HeaderBrush"] = Color.FromRgb(24, 33, 42),
        ["HeaderBorderBrush"] = Color.FromRgb(38, 52, 64),
        ["RowHoverBrush"] = Color.FromRgb(27, 39, 50),
        ["ScrollThumbBrush"] = Color.FromRgb(65, 83, 102),
        ["ScrollThumbHoverBrush"] = Color.FromRgb(88, 112, 134),
        ["ScrollTrackBrush"] = Color.FromRgb(17, 24, 32),
        ["ScrollTrackHoverBrush"] = Color.FromRgb(21, 31, 40),
        ["ToolTipBrush"] = Color.FromRgb(28, 39, 50),
        ["ProgressTrackBrush"] = Color.FromRgb(32, 42, 52),
        ["WindowHeaderBrush"] = Color.FromRgb(14, 20, 26),
        ["LoadingOverlayBrush"] = Color.FromArgb(204, 11, 15, 20),
        ["WarningSurfaceBrush"] = Color.FromRgb(43, 33, 20),
        ["WarningBorderBrush"] = Color.FromRgb(107, 83, 40),
        ["WarningTextBrush"] = Color.FromRgb(255, 217, 146),
        ["DangerSurfaceBrush"] = Color.FromRgb(59, 32, 35),
        ["DangerBorderBrush"] = Color.FromRgb(127, 53, 59),
        ["DangerTextBrush"] = Color.FromRgb(255, 216, 218),
        ["CloseHoverBrush"] = Color.FromRgb(196, 43, 28),
        ["CloseTextBrush"] = Color.FromRgb(255, 183, 186),
        ["RingTrackBrush"] = Color.FromRgb(35, 47, 58),
        ["ChartGridBrush"] = Color.FromRgb(32, 43, 53),
        ["ChartTextBrush"] = Color.FromRgb(242, 246, 248),
        ["BlueAccentBrush"] = Color.FromRgb(78, 161, 255),
        ["BlueSurfaceBrush"] = Color.FromRgb(37, 49, 72)
    };

    private static readonly Dictionary<string, Color> LightPalette = new()
    {
        ["WindowBrush"] = Color.FromRgb(244, 247, 250),
        ["SidebarBrush"] = Color.FromRgb(255, 255, 255),
        ["CardBrush"] = Color.FromRgb(255, 255, 255),
        ["CardHoverBrush"] = Color.FromRgb(241, 245, 249),
        ["BorderBrushSoft"] = Color.FromRgb(214, 224, 232),
        ["TextPrimaryBrush"] = Color.FromRgb(23, 33, 43),
        ["TextSecondaryBrush"] = Color.FromRgb(91, 107, 122),
        ["TextMutedBrush"] = Color.FromRgb(137, 152, 166),
        ["AccentBrush"] = Color.FromRgb(37, 111, 232),
        ["AccentTextBrush"] = Color.FromRgb(255, 255, 255),
        ["WarmBrush"] = Color.FromRgb(201, 133, 24),
        ["DangerBrush"] = Color.FromRgb(201, 61, 75),
        ["HoverBrush"] = Color.FromRgb(234, 240, 245),
        ["PressedBrush"] = Color.FromRgb(220, 230, 238),
        ["SecondaryButtonBrush"] = Color.FromRgb(233, 239, 244),
        ["SecondaryBorderBrush"] = Color.FromRgb(199, 211, 221),
        ["ElementHoverBrush"] = Color.FromRgb(220, 230, 238),
        ["ElementPressedBrush"] = Color.FromRgb(204, 217, 227),
        ["InputBrush"] = Color.FromRgb(247, 249, 251),
        ["InputBorderBrush"] = Color.FromRgb(197, 209, 220),
        ["PanelInsetBrush"] = Color.FromRgb(247, 249, 251),
        ["BadgeBackgroundBrush"] = Color.FromRgb(225, 236, 255),
        ["BadgeBorderBrush"] = Color.FromRgb(139, 182, 245),
        ["GridLineBrush"] = Color.FromRgb(220, 229, 236),
        ["HeaderBrush"] = Color.FromRgb(234, 240, 245),
        ["HeaderBorderBrush"] = Color.FromRgb(204, 217, 227),
        ["RowHoverBrush"] = Color.FromRgb(239, 244, 248),
        ["ScrollThumbBrush"] = Color.FromRgb(157, 176, 191),
        ["ScrollThumbHoverBrush"] = Color.FromRgb(116, 138, 155),
        ["ScrollTrackBrush"] = Color.FromRgb(228, 235, 241),
        ["ScrollTrackHoverBrush"] = Color.FromRgb(218, 228, 236),
        ["ToolTipBrush"] = Color.FromRgb(255, 255, 255),
        ["ProgressTrackBrush"] = Color.FromRgb(222, 231, 238),
        ["WindowHeaderBrush"] = Color.FromRgb(255, 255, 255),
        ["LoadingOverlayBrush"] = Color.FromArgb(204, 244, 247, 250),
        ["WarningSurfaceBrush"] = Color.FromRgb(255, 244, 216),
        ["WarningBorderBrush"] = Color.FromRgb(231, 197, 107),
        ["WarningTextBrush"] = Color.FromRgb(117, 80, 0),
        ["DangerSurfaceBrush"] = Color.FromRgb(255, 232, 234),
        ["DangerBorderBrush"] = Color.FromRgb(222, 137, 145),
        ["DangerTextBrush"] = Color.FromRgb(151, 39, 52),
        ["CloseHoverBrush"] = Color.FromRgb(196, 43, 28),
        ["CloseTextBrush"] = Color.FromRgb(151, 39, 52),
        ["RingTrackBrush"] = Color.FromRgb(220, 229, 236),
        ["ChartGridBrush"] = Color.FromRgb(220, 229, 236),
        ["ChartTextBrush"] = Color.FromRgb(23, 33, 43),
        ["BlueAccentBrush"] = Color.FromRgb(78, 161, 255),
        ["BlueSurfaceBrush"] = Color.FromRgb(225, 236, 252)
    };
}
