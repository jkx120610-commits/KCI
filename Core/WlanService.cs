using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace KCI.Core;

public sealed class WlanService
{
    private readonly object _sync = new();
    private WlanSnapshot _cached = new();
    private DateTime _lastConnectionRefresh = DateTime.MinValue;
    private DateTime _lastTrafficSample = DateTime.MinValue;
    private long _lastReceivedBytes;
    private long _lastSentBytes;

    public WlanSnapshot Sample(bool forceConnectionRefresh = false)
    {
        lock (_sync)
        {
            if (forceConnectionRefresh ||
                DateTime.UtcNow - _lastConnectionRefresh >= TimeSpan.FromSeconds(5))
            {
                _cached = ReadConnection();
                _lastConnectionRefresh = DateTime.UtcNow;
            }

            var networkInterface = FindWirelessInterface();
            if (networkInterface is not null)
            {
                try
                {
                    var statistics = networkInterface.GetIPStatistics();
                    var now = DateTime.UtcNow;
                    if (_lastTrafficSample != DateTime.MinValue)
                    {
                        var seconds = Math.Max(
                            0.25, (now - _lastTrafficSample).TotalSeconds);
                        _cached.DownloadMbps = Math.Max(
                            0, (statistics.BytesReceived - _lastReceivedBytes) * 8d /
                               seconds / 1_000_000d);
                        _cached.UploadMbps = Math.Max(
                            0, (statistics.BytesSent - _lastSentBytes) * 8d /
                               seconds / 1_000_000d);
                    }
                    _lastReceivedBytes = statistics.BytesReceived;
                    _lastSentBytes = statistics.BytesSent;
                    _lastTrafficSample = now;
                    _cached.ReceivedBytes = statistics.BytesReceived;
                    _cached.SentBytes = statistics.BytesSent;
                }
                catch
                {
                    // Traffic counters can be unavailable during adapter reset.
                }
            }

            return _cached;
        }
    }

    public string GetDefaultGateway()
    {
        return FindWirelessInterface()?
            .GetIPProperties()
            .GatewayAddresses
            .Select(gateway => gateway.Address)
            .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork)
            ?.ToString() ?? string.Empty;
    }

    public async Task<string> ResolveRouterUrlAsync(CancellationToken cancellationToken = default)
    {
        var gateway = GetDefaultGateway();
        if (string.IsNullOrWhiteSpace(gateway))
        {
            return string.Empty;
        }

        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };
        foreach (var scheme in new[] { "http", "https" })
        {
            var url = $"{scheme}://{gateway}/";
            try
            {
                using var response = await client.GetAsync(
                    url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if ((int)response.StatusCode is >= 200 and < 500)
                {
                    return url;
                }
            }
            catch
            {
                // Try the next scheme.
            }
        }

        return $"http://{gateway}/";
    }

    private static WlanSnapshot ReadConnection()
    {
        var snapshot = new WlanSnapshot();
        var networkInterface = FindWirelessInterface();
        if (networkInterface is null)
        {
            return snapshot;
        }

        snapshot.AdapterName = networkInterface.Name;
        snapshot.MacAddress = networkInterface.GetPhysicalAddress()
            .GetAddressBytes()
            .Select(value => value.ToString("X2"))
            .Aggregate((left, right) => left + ":" + right);
        var properties = networkInterface.GetIPProperties();
        snapshot.IpAddress = properties.UnicastAddresses
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(address => address.Address.ToString())
            .FirstOrDefault() ?? "未知";
        snapshot.Gateway = properties.GatewayAddresses
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(address => address.Address.ToString())
            .FirstOrDefault() ?? "未知";

        snapshot.Connected = true;
        ApplyNetshConnection(snapshot);
        if (snapshot.Connected && snapshot.SignalQuality >= 0)
        {
            snapshot.SecurityLevel = SecurityLevel(
                snapshot.Authentication, snapshot.Cipher);
        }
        else
        {
            snapshot.SecurityLevel = "未知";
        }
        return snapshot;
    }

    private static void ApplyNetshConnection(WlanSnapshot snapshot)
    {
        try
        {
            var output = RunCommand("netsh.exe", "wlan show interfaces");
            foreach (var line in output.Split('\n'))
            {
                var separator = line.IndexOf(':');
                if (separator < 0)
                {
                    continue;
                }
                var key = line[..separator].Trim().ToLowerInvariant();
                var value = line[(separator + 1)..].Trim();
                if (key.Contains("ssid") && !key.Contains("bssid"))
                {
                    snapshot.Ssid = value;
                }
                else if (key.Contains("状态") || key.Contains("state"))
                {
                    snapshot.Connected =
                        value.Contains("已连接", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains("connected", StringComparison.OrdinalIgnoreCase);
                }
                else if (key.Contains("bssid"))
                {
                    snapshot.Bssid = value;
                }
                else if (key.Contains("信号") || key.Contains("signal"))
                {
                    snapshot.SignalQuality = ParsePercent(value, snapshot.SignalQuality);
                }
                else if (key.Contains("rssi"))
                {
                    var match = Regex.Match(value, @"-?\d+");
                    if (match.Success && int.TryParse(match.Value, out var rssi))
                    {
                        snapshot.Rssi = rssi;
                    }
                }
                else if (key.Contains("无线") || key.Contains("radio"))
                {
                    snapshot.PhyType = value;
                }
                else if (key.Contains("信道") || key.Contains("通道") || key.Contains("channel"))
                {
                    snapshot.Channel = value;
                }
                else if (key.Contains("频段") || key.Contains("波段") || key.Contains("band"))
                {
                    snapshot.Band = value;
                }
                else if (key.Contains("authentication") || key.Contains("身份验证") ||
                         key.Contains("认证"))
                {
                    snapshot.Authentication = value;
                }
                else if (key.Contains("cipher") || key.Contains("密码") || key.Contains("加密"))
                {
                    snapshot.Cipher = value;
                }
                else if (key.Contains("receive rate") || key.Contains("接收速率"))
                {
                    snapshot.ReceiveMbps = ParseRate(value);
                }
                else if (key.Contains("transmit rate") || key.Contains("传输速率") ||
                         key.Contains("发送速率"))
                {
                    snapshot.TransmitMbps = ParseRate(value);
                }
            }
        }
        catch
        {
            // netsh is only a localization fallback.
        }

        if (snapshot.Connected && string.IsNullOrWhiteSpace(snapshot.Band))
        {
            snapshot.Band = snapshot.Channel switch
            {
                var channel when int.TryParse(channel, out var number) && number <= 14 => "2.4 GHz",
                var channel when int.TryParse(channel, out var number) && number >= 32 => "5 GHz / 6 GHz",
                _ => "未知"
            };
        }
        snapshot.SecurityLevel = SecurityLevel(snapshot.Authentication, snapshot.Cipher);
        snapshot.SecurityAdvice = SecurityAdvice(snapshot.SecurityLevel);
    }

    private static NetworkInterface? FindWirelessInterface()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(network => network.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                                  network.OperationalStatus == OperationalStatus.Up)
                .OrderByDescending(network => network.GetIPProperties().GatewayAddresses.Count)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool GuidMatches(Guid guid, string interfaceId)
    {
        return Guid.TryParse(interfaceId.Trim('{', '}'), out var parsed) &&
               parsed == guid;
    }

    private static string FormatMac(byte[]? bytes)
    {
        return bytes is null || bytes.Length < 6
            ? "未知"
            : string.Join(":", bytes.Take(6).Select(value => value.ToString("X2")));
    }

    private static string PhyTypeText(uint value) => value switch
    {
        4 => "802.11a / OFDM",
        7 => "802.11n",
        8 => "802.11a",
        9 => "802.11b",
        10 => "802.11g",
        11 => "802.11ac",
        12 => "802.11ad",
        13 => "802.11ax (Wi-Fi 6)",
        14 => "802.11be (Wi-Fi 7)",
        _ => "未知"
    };

    private static string AuthenticationText(uint value) => value switch
    {
        1 => "Open",
        2 => "Shared Key",
        3 => "WPA",
        4 => "WPA-PSK",
        5 => "WPA-None",
        6 => "WPA2-Enterprise",
        7 => "WPA2-Personal",
        8 => "WPA3-Enterprise",
        9 => "WPA3-Personal",
        10 => "WPA3-Enterprise 192-bit",
        _ => "未知"
    };

    private static string CipherText(uint value) => value switch
    {
        1 => "无",
        2 => "WEP-40",
        3 => "TKIP",
        4 => "CCMP",
        5 => "WEP-104",
        6 => "BIP",
        7 => "GCMP",
        _ => "未知"
    };

    private static string SecurityLevel(string authentication, string cipher)
    {
        var combined = $"{authentication} {cipher}".ToLowerInvariant();
        if (combined.Contains("wpa3") || combined.Contains("gcmp"))
        {
            return "优秀";
        }
        if (combined.Contains("wpa2") || combined.Contains("ccmp"))
        {
            return "良好";
        }
        if (combined.Contains("wpa") || combined.Contains("tkip"))
        {
            return "一般";
        }
        if (combined.Contains("wep") || combined.Contains("open"))
        {
            return "风险";
        }
        return "未知";
    }

    private static int ParsePercent(string value, int fallback)
    {
        var match = Regex.Match(value, @"-?\d+");
        return match.Success && int.TryParse(match.Value, out var number)
            ? Math.Clamp(number, 0, 100)
            : fallback;
    }

    private static double ParseRate(string value)
    {
        var match = Regex.Match(value, @"\d+(?:\.\d+)?");
        return match.Success && double.TryParse(
            match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate)
            ? rate
            : double.NaN;
    }

    private static string SecurityAdvice(string level) => level switch
    {
        "优秀" => "WPA3 / GCMP，安全配置良好。",
        "良好" => "WPA2 / CCMP，常规安全配置。",
        "一般" => "使用较旧加密协议，建议路由器升级到 WPA2/WPA3。",
        "风险" => "网络可能是开放或使用 WEP，建议立即更换安全配置。",
        _ => "无法判断安全等级，请检查路由器加密设置。"
    };

    private static string RunCommand(string fileName, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        });
        if (process is null)
        {
            return string.Empty;
        }
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(5000);
        return output;
    }
}
