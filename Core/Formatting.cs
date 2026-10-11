using System.Globalization;
using System.Text;

namespace KCI.Core;

public static class Formatting
{
    public static string Percent(double value, int decimals = 1)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? "--"
            : $"{value.ToString($"F{decimals}", CultureInfo.InvariantCulture)}%";
    }

    public static string Temperature(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? "--"
            : $"{value:0.#} °C";
    }

    public static string Number(double value, string unit, int decimals = 1)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? "--"
            : $"{value.ToString($"F{decimals}", CultureInfo.InvariantCulture)} {unit}";
    }

    public static string Bytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        var value = (double)Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    public static string Compact(string? value, int maxLength = 80)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "未知";
        }

        var normalized = string.Join(" ", value.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..Math.Max(0, maxLength - 1)] + "…";
    }

    public static string SafeFileName(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value)
        {
            builder.Append(Path.GetInvalidFileNameChars().Contains(character) ? '_' : character);
        }
        return builder.ToString();
    }
}
