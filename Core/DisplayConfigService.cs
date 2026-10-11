using System.Runtime.InteropServices;

namespace KCI.Core;

public sealed class DisplayEndpointInfo
{
    public string DeviceName { get; init; } = string.Empty;
    public string FriendlyName { get; init; } = string.Empty;
    public string InterfaceType { get; init; } = "系统未报告";
    public ushort EdidManufacturerId { get; init; }
    public ushort EdidProductCodeId { get; init; }
}

public static class DisplayConfigService
{
    public static IReadOnlyList<DisplayEndpointInfo> GetActiveDisplays()
    {
        var result = new List<DisplayEndpointInfo>();
        try
        {
            var status = NativeMethods.GetDisplayConfigBufferSizes(
                NativeMethods.QdcOnlyActivePaths, out var pathCount, out var modeCount);
            if (status != 0 || pathCount == 0)
            {
                return result;
            }

            var paths = new NativeMethods.DisplayConfigPathInfo[pathCount];
            var modes = new NativeMethods.DisplayConfigModeInfo[modeCount];
            status = NativeMethods.QueryDisplayConfig(
                NativeMethods.QdcOnlyActivePaths,
                ref pathCount,
                paths,
                ref modeCount,
                modes,
                IntPtr.Zero);
            if (status != 0)
            {
                return result;
            }

            for (var index = 0; index < pathCount; index++)
            {
                var path = paths[index];
                var source = new NativeMethods.DisplayConfigSourceDeviceName
                {
                    Header = new NativeMethods.DisplayConfigDeviceInfoHeader
                    {
                        Type = NativeMethods.DisplayConfigDeviceInfoGetSourceName,
                        Size = (uint)Marshal.SizeOf<NativeMethods.DisplayConfigSourceDeviceName>(),
                        AdapterId = path.SourceInfo.AdapterId,
                        Id = path.SourceInfo.Id
                    }
                };
                if (NativeMethods.DisplayConfigGetDeviceInfo(ref source) != 0 ||
                    string.IsNullOrWhiteSpace(source.ViewGdiDeviceName))
                {
                    continue;
                }

                var target = new NativeMethods.DisplayConfigTargetDeviceName
                {
                    Header = new NativeMethods.DisplayConfigDeviceInfoHeader
                    {
                        Type = NativeMethods.DisplayConfigDeviceInfoGetTargetName,
                        Size = (uint)Marshal.SizeOf<NativeMethods.DisplayConfigTargetDeviceName>(),
                        AdapterId = path.TargetInfo.AdapterId,
                        Id = path.TargetInfo.Id
                    }
                };
                var targetStatus = NativeMethods.DisplayConfigGetDeviceInfo(ref target);

                result.Add(new DisplayEndpointInfo
                {
                    DeviceName = source.ViewGdiDeviceName,
                    FriendlyName = targetStatus == 0
                        ? target.MonitorFriendlyDeviceName?.Trim() ?? string.Empty
                        : string.Empty,
                    InterfaceType = OutputTechnologyText(path.TargetInfo.OutputTechnology),
                    EdidManufacturerId = targetStatus == 0 ? target.EdidManufactureId : (ushort)0,
                    EdidProductCodeId = targetStatus == 0 ? target.EdidProductCodeId : (ushort)0
                });
            }
        }
        catch
        {
            // Windows versions without DisplayConfig fall back to EDID and EnumDisplayMonitors.
        }
        return result;
    }

    private static string OutputTechnologyText(uint outputTechnology) => outputTechnology switch
    {
        0 => "其他接口",
        1 => "VGA",
        2 => "S-Video",
        3 => "Composite",
        4 => "Component",
        5 => "DVI",
        6 => "HDMI",
        7 => "eDP（内置）",
        8 => "D-Terminal",
        9 => "SDI",
        10 => "DisplayPort",
        11 => "eDP（内置）",
        12 => "UDI 外接",
        13 => "UDI 内置",
        14 => "SDTV",
        15 => "Miracast / 无线显示",
        16 => "间接有线显示",
        17 => "虚拟显示器",
        18 => "USB-C DisplayPort",
        19 => "USB-C DisplayPort",
        _ => $"其他接口 ({outputTechnology})"
    };
}
