using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace KCI.Core;

public sealed class LanScannerService
{
    private static readonly Regex ArpRegex = new(
        @"^\s*(?<ip>\d{1,3}(?:\.\d{1,3}){3})\s+(?<mac>(?:[0-9a-fA-F]{2}[-:]){5}[0-9a-fA-F]{2})",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public async Task<IReadOnlyList<LanDeviceInfo>> ScanAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var localAddresses = GetLocalAddresses();
        if (localAddresses.Count == 0)
        {
            return [];
        }

        var candidates = BuildScanAddresses().ToList();
        progress?.Report($"正在扫描 {candidates.Count} 个局域网地址...");

        using var gate = new SemaphoreSlim(48);
        var responsive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tasks = candidates.Select(async ip =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, 260);
                if (reply.Status == IPStatus.Success)
                {
                    lock (responsive)
                    {
                        responsive.Add(ip);
                    }
                }
            }
            catch
            {
                // Unreachable hosts are expected.
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks);

        var arp = ReadArpTable();
        var result = arp.Values
            .Where(device => IsInLocalSubnet(device.IpAddress, localAddresses))
            .Where(device => !device.IpAddress.EndsWith(".255", StringComparison.OrdinalIgnoreCase) &&
                             !device.IpAddress.EndsWith(".0", StringComparison.OrdinalIgnoreCase) &&
                             !device.MacAddress.Equals("FF:FF:FF:FF:FF:FF", StringComparison.OrdinalIgnoreCase) &&
                             !device.MacAddress.Equals("00:00:00:00:00:00", StringComparison.OrdinalIgnoreCase) &&
                             !device.MacAddress.StartsWith("01:00:5E", StringComparison.OrdinalIgnoreCase))
            .Select(device =>
            {
                var isLocal = localAddresses.Any(address =>
                    address.Address.ToString().Equals(
                        device.IpAddress, StringComparison.OrdinalIgnoreCase));
                return new LanDeviceInfo
                {
                    IpAddress = device.IpAddress,
                    MacAddress = device.MacAddress,
                    Responsive = responsive.Contains(device.IpAddress) || isLocal,
                    Role = isLocal ? "本机" : "局域网设备",
                    Vendor = GuessVendor(device.MacAddress)
                };
            })
            .GroupBy(device => device.IpAddress)
            .Select(group => group.First())
            .ToList();

        progress?.Report("正在解析设备名称...");
        var withNames = new List<LanDeviceInfo>();
        foreach (var device in result.OrderBy(device => IPAddress.Parse(device.IpAddress).GetAddressBytes()[3]))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hostName = string.Empty;
            try
            {
                var lookup = Dns.GetHostEntryAsync(device.IpAddress)
                    .WaitAsync(TimeSpan.FromMilliseconds(600), cancellationToken);
                var entry = await lookup;
                hostName = entry.HostName;
            }
            catch
            {
                // Host names are frequently hidden by the router.
            }

            var gateway = localAddresses.Any(address =>
                address.Gateway?.ToString().Equals(
                    device.IpAddress, StringComparison.OrdinalIgnoreCase) == true);
            withNames.Add(new LanDeviceInfo
            {
                IpAddress = device.IpAddress,
                MacAddress = device.MacAddress,
                HostName = string.IsNullOrWhiteSpace(hostName) ? "未解析" : hostName,
                Role = gateway ? "默认网关 / 路由器" : device.Role,
                Vendor = device.Vendor,
                Responsive = device.Responsive
            });
        }
        progress?.Report($"扫描完成，发现 {withNames.Count} 个 ARP/响应设备。");
        return withNames;
    }

    private static List<LocalAddressInfo> GetLocalAddresses()
    {
        var result = new List<LocalAddressInfo>();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            var properties = networkInterface.GetIPProperties();
            var gateway = properties.GatewayAddresses
                .Select(item => item.Address)
                .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);
            foreach (var unicast in properties.UnicastAddresses
                         .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork))
            {
                result.Add(new LocalAddressInfo(unicast.Address, unicast.IPv4Mask, gateway));
            }
        }
        return result;
    }

    private static Dictionary<string, ArpDevice> ReadArpTable()
    {
        var result = new Dictionary<string, ArpDevice>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var process = Process.Start(new ProcessStartInfo("arp.exe", "-a")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            });
            if (process is null)
            {
                return result;
            }
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            foreach (Match match in ArpRegex.Matches(output))
            {
                var ip = match.Groups["ip"].Value;
                result[ip] = new ArpDevice
                {
                    IpAddress = ip,
                    MacAddress = match.Groups["mac"].Value.Replace('-', ':').ToUpperInvariant()
                };
            }
        }
        catch
        {
            // ARP output is optional when using a restricted network profile.
        }
        return result;
    }

    private static IEnumerable<string> BuildScanAddresses()
    {
        var local = GetLocalSubnetInfo();
        if (local is null)
        {
            return [];
        }

        var addressBytes = local.Address.GetAddressBytes();
        var maskBytes = local.Mask?.GetAddressBytes() ?? new byte[] { 255, 255, 255, 0 };
        var network = new byte[4];
        for (var index = 0; index < 4; index++)
        {
            network[index] = (byte)(addressBytes[index] & maskBytes[index]);
        }

        var addresses = new List<string>();
        var prefixLength = maskBytes.Sum(value => Convert.ToString(value, 2).Count(bit => bit == '1'));
        if (prefixLength < 24)
        {
            network[3] = 0;
        }
        for (var host = 1; host <= 254; host++)
        {
            addresses.Add($"{network[0]}.{network[1]}.{network[2]}.{host}");
        }
        if (local.Gateway is not null)
        {
            addresses.Add(local.Gateway.ToString());
        }
        return addresses.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static LocalAddressInfo? GetLocalSubnetInfo()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(network => network.OperationalStatus == OperationalStatus.Up &&
                                  network.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(network => network.GetIPProperties().UnicastAddresses
                    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => new LocalAddressInfo(
                        address.Address,
                        address.IPv4Mask,
                        network.GetIPProperties().GatewayAddresses
                            .Select(gateway => gateway.Address)
                            .FirstOrDefault(item => item.AddressFamily == AddressFamily.InterNetwork))))
                .OrderByDescending(item => item.Address.ToString().StartsWith("192.168."))
                .ThenByDescending(item => item.Address.ToString().StartsWith("10."))
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsInLocalSubnet(
        string ip,
        IReadOnlyCollection<LocalAddressInfo> localAddresses)
    {
        return localAddresses.Any(local =>
        {
            var address = IPAddress.Parse(ip).GetAddressBytes();
            var localBytes = local.Address.GetAddressBytes();
            var mask = local.Mask?.GetAddressBytes() ?? new byte[] { 255, 255, 255, 0 };
            return address.Zip(mask, (value, maskValue) => (value & maskValue))
                .SequenceEqual(localBytes.Zip(mask, (value, maskValue) => (value & maskValue)));
        });
    }

    private static string GuessVendor(string mac)
    {
        var prefix = mac.Replace(":", string.Empty).ToUpperInvariant();
        if (prefix.StartsWith("F0A731") || prefix.StartsWith("7CDFA1"))
        {
            return "TP-Link / 网络设备";
        }
        if (prefix.StartsWith("A4C138") || prefix.StartsWith("8CAA"))
        {
            return "小米 / Xiaomi";
        }
        if (prefix.StartsWith("00E04C") || prefix.StartsWith("3C5A37"))
        {
            return "华为 / HUAWEI";
        }
        if (prefix.StartsWith("D850E6") || prefix.StartsWith("A85C2C"))
        {
            return "Apple";
        }
        if (prefix.StartsWith("001A79") || prefix.StartsWith("9C5CF9"))
        {
            return "Samsung";
        }
        return "未知厂商";
    }

    private sealed record LocalAddressInfo(
        IPAddress Address,
        IPAddress? Mask,
        IPAddress? Gateway);

    private sealed class ArpDevice
    {
        public string IpAddress { get; init; } = string.Empty;
        public string MacAddress { get; init; } = string.Empty;
    }
}
