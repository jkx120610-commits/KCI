using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace KCI.Core;

public sealed class SpeedTestService
{
    private const int ParallelConnections = 3;
    private const double DownloadWindowSeconds = 4;
    private const double UploadWindowSeconds = 3;

    public async Task<SpeedTestResult> RunAsync(
        IProgress<SpeedTestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Report(progress, "准备测速", 0, 0, 0, 0, "正在准备网络测速...");
            var latency = await MeasureLatencyAsync(progress, cancellationToken);
            Report(progress, "下载测速", 12, 100, 0, 0, "正在测试下载速度...");
            var download = await MeasureDownloadAsync(progress, cancellationToken);
            Report(progress, "上传测速", 55, 100, 100, 0, "正在测试上传速度...");

            double upload;
            try
            {
                upload = await MeasureUploadAsync(progress, cancellationToken);
            }
            catch
            {
                upload = double.NaN;
            }

            Report(progress, "测速完成", 100, 100, 100, 100, "测速完成");
            return new SpeedTestResult
            {
                Success = !double.IsNaN(download) || !double.IsNaN(upload),
                LatencyMs = latency,
                DownloadMbps = download,
                UploadMbps = upload,
                Message = double.IsNaN(upload)
                    ? "下载测速完成；上传端点当前不可用"
                    : "测速完成"
            };
        }
        catch (Exception ex)
        {
            return new SpeedTestResult
            {
                Success = false,
                Message = $"测速失败：{ex.Message}"
            };
        }
    }

    private static async Task<double> MeasureLatencyAsync(
        IProgress<SpeedTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        var values = new List<long>();
        using var ping = new Ping();
        const int sampleCount = 3;
        for (var index = 0; index < sampleCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync("1.1.1.1", 1200);
                if (reply.Status == IPStatus.Success)
                {
                    values.Add(reply.RoundtripTime);
                }
            }
            catch
            {
                // A failed sample should not fail the complete test.
            }

            var latencyPercent = (index + 1) * 100d / sampleCount;
            Report(progress, "延迟测试", latencyPercent * 0.12,
                latencyPercent, 0, 0, "正在测试网络延迟...");
        }
        if (values.Count > 0 && values.Average() > 0)
        {
            return values.Average();
        }

        var tcpSamples = new List<double>();
        for (var index = 0; index < 3; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync("1.1.1.1", 443, cancellationToken)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(1.5), cancellationToken);
                stopwatch.Stop();
                tcpSamples.Add(stopwatch.Elapsed.TotalMilliseconds);
            }
            catch
            {
                // Continue with the remaining samples.
            }
        }
        return tcpSamples.Count > 0 ? tcpSamples.Average() : double.NaN;
    }

    private static async Task<double> MeasureDownloadAsync(
        IProgress<SpeedTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (CurlPath() is not null)
        {
            return await MeasureCurlDownloadAsync(progress, cancellationToken);
        }

        using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        window.CancelAfter(TimeSpan.FromSeconds(DownloadWindowSeconds));
        long totalBytes = 0;
        using var client = CreateClient();
        if (!await WarmUpAsync(client, cancellationToken))
        {
            return await MeasureCurlDownloadAsync(progress, cancellationToken);
        }
        var stopwatch = Stopwatch.StartNew();
        var workers = Enumerable.Range(0, ParallelConnections)
            .Select(index => DownloadWorkerAsync(client, index, window.Token, bytes =>
                Interlocked.Add(ref totalBytes, bytes)))
            .ToList();
        var allWorkers = Task.WhenAll(workers);

        while (!allWorkers.IsCompleted && !cancellationToken.IsCancellationRequested)
        {
            var elapsed = Math.Min(DownloadWindowSeconds, stopwatch.Elapsed.TotalSeconds);
            var downloadPercent = Math.Clamp(
                elapsed / DownloadWindowSeconds * 100d, 0, 100);
            Report(progress, "下载测速", 12 + downloadPercent * 0.43,
                Math.Max(100, downloadPercent), downloadPercent, 0,
                $"下载中 · {Interlocked.Read(ref totalBytes) * 8d / 1024 / 1024:0.0} Mb");
            await Task.WhenAny(allWorkers, Task.Delay(100, cancellationToken));
        }

        try
        {
            await allWorkers;
        }
        catch (OperationCanceledException) when (window.IsCancellationRequested)
        {
            // The fixed test window ended.
        }
        catch
        {
            // Keep any bytes that were measured successfully.
        }

        stopwatch.Stop();
        var finalBytes = Interlocked.Read(ref totalBytes);
        if (finalBytes == 0)
        {
            return await MeasureCurlDownloadAsync(progress, cancellationToken);
        }
        var result = finalBytes > 0
            ? finalBytes * 8d / Math.Max(0.1, stopwatch.Elapsed.TotalSeconds) / 1_000_000d
            : double.NaN;
        Report(progress, "下载测速完成", 55, 100, 100, 0,
            $"下载完成 · {result:0.0} Mbps");
        return result;
    }

    private static async Task<long> DownloadWorkerAsync(
        HttpClient client,
        int index,
        CancellationToken cancellationToken,
        Action<int> onBytes)
    {
        try
        {
            var url = $"https://speed.cloudflare.com/__down?bytes=100000000&r={Guid.NewGuid():N}";
            using var response = await client.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[128 * 1024];
            long localBytes = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read <= 0)
                {
                    break;
                }
                localBytes += read;
                onBytes(read);
            }
            return localBytes;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private static async Task<bool> WarmUpAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var response = await client.GetAsync(
                    "https://speed.cloudflare.com/__down?bytes=1024",
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch
            {
                await Task.Delay(250, cancellationToken);
            }
        }
        return false;
    }

    private static async Task<double> MeasureSingleDownloadAsync(
        CancellationToken cancellationToken)
    {
        var urls = new[]
        {
            "https://speed.cloudflare.com/__down?bytes=100000000",
            "https://proof.ovh.net/files/100Mb.dat"
        };
        foreach (var url in urls)
        {
            try
            {
                using var client = CreateClient();
                using var response = await client.GetAsync(
                    url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var buffer = new byte[128 * 1024];
                var stopwatch = Stopwatch.StartNew();
                long bytes = 0;
                while (stopwatch.Elapsed < TimeSpan.FromSeconds(4))
                {
                    var read = await stream.ReadAsync(buffer, cancellationToken);
                    if (read <= 0)
                    {
                        break;
                    }
                    bytes += read;
                }
                stopwatch.Stop();
                if (bytes > 0)
                {
                    return bytes * 8d /
                           Math.Max(0.1, stopwatch.Elapsed.TotalSeconds) / 1_000_000d;
                }
            }
            catch
            {
                // Try the next endpoint.
            }
        }
        return double.NaN;
    }

    private static async Task<double> MeasureCurlDownloadAsync(
        IProgress<SpeedTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        var curl = CurlPath();
        if (curl is null)
        {
            return double.NaN;
        }

        foreach (var url in new[]
                 {
                     "https://proof.ovh.net/files/100Mb.dat",
                     "https://speed.cloudflare.com/__down?bytes=100000000"
                 })
        {
            var stopwatch = Stopwatch.StartNew();
            var output = await RunCurlAsync(
                curl,
                [
                    "--location", "--http1.1", "--silent", "--show-error",
                    "--max-time", "3", "--output", "NUL", "--write-out",
                    "%{size_download}", url
                ],
                percentage => Report(progress, "下载测速",
                    12 + percentage * 0.43, 100, percentage, 0,
                    $"下载中 · {percentage:0}%"),
                cancellationToken);
            stopwatch.Stop();
            if (double.TryParse(output, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var bytes) &&
                bytes > 128 * 1024)
            {
                return bytes * 8d /
                       Math.Max(0.1, stopwatch.Elapsed.TotalSeconds) / 1_000_000d;
            }
        }
        return double.NaN;
    }

    private static async Task<double> MeasureUploadAsync(
        IProgress<SpeedTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (CurlPath() is not null)
        {
            return await MeasureCurlUploadAsync(progress, cancellationToken);
        }

        using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        window.CancelAfter(TimeSpan.FromSeconds(UploadWindowSeconds));
        const int bytesPerWorker = 1536 * 1024;
        long totalBytes = 0;
        using var client = CreateClient();
        var stopwatch = Stopwatch.StartNew();
        var workers = Enumerable.Range(0, 2)
            .Select(index => UploadWorkerAsync(client, bytesPerWorker, window.Token, bytes =>
                Interlocked.Add(ref totalBytes, bytes)))
            .ToList();
        var allWorkers = Task.WhenAll(workers);

        while (!allWorkers.IsCompleted && !cancellationToken.IsCancellationRequested)
        {
            var elapsed = Math.Min(UploadWindowSeconds, stopwatch.Elapsed.TotalSeconds);
            var uploadPercent = Math.Clamp(
                elapsed / UploadWindowSeconds * 100d, 0, 100);
            Report(progress, "上传测速", 55 + uploadPercent * 0.43,
                100, 100, uploadPercent,
                $"上传中 · {Interlocked.Read(ref totalBytes) * 8d / 1024 / 1024:0.0} Mb");
            await Task.WhenAny(allWorkers, Task.Delay(100, cancellationToken));
        }

        try
        {
            await allWorkers;
        }
        catch
        {
            // Partial upload measurements are still useful.
        }

        stopwatch.Stop();
        var finalBytes = Interlocked.Read(ref totalBytes);
        if (finalBytes == 0)
        {
            return await MeasureCurlUploadAsync(progress, cancellationToken);
        }
        var result = finalBytes > 0
            ? finalBytes * 8d / Math.Max(0.1, stopwatch.Elapsed.TotalSeconds) / 1_000_000d
            : double.NaN;
        Report(progress, "上传测速完成", 98, 100, 100, 100,
            $"上传完成 · {result:0.0} Mbps");
        return result;
    }

    private static async Task<double> MeasureCurlUploadAsync(
        IProgress<SpeedTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        var curl = CurlPath();
        if (curl is null)
        {
            return double.NaN;
        }

        var payloadPath = Path.Combine(
            Path.GetTempPath(), $"kci-speedtest-{Environment.ProcessId}.bin");
        try
        {
            const long payloadSize = 6L * 1024 * 1024;
            await using (var stream = new FileStream(
                             payloadPath, FileMode.Create, FileAccess.Write,
                             FileShare.Read, 64 * 1024, true))
            {
                stream.SetLength(payloadSize);
            }

            foreach (var url in new[]
                     {
                         "http://speedtest.tele2.net/upload.php",
                         "https://speed.cloudflare.com/__up"
                     })
            {
                var stopwatch = Stopwatch.StartNew();
                var output = await RunCurlAsync(
                    curl,
                    [
                        "--location", "--http1.1", "--silent", "--show-error",
                        "--max-time", "3", "--request", "POST",
                        "--header", "Content-Type: application/octet-stream",
                        "--data-binary", "@" + payloadPath,
                        "--output", "NUL", "--write-out", "%{size_upload}", url
                    ],
                    percentage => Report(progress, "上传测速",
                        55 + percentage * 0.43, 100, 100, percentage,
                        $"上传中 · {percentage:0}%"),
                    cancellationToken);
                stopwatch.Stop();
                if (double.TryParse(output, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var uploaded) &&
                    uploaded > 0)
                {
                    return uploaded * 8d /
                           Math.Max(0.1, stopwatch.Elapsed.TotalSeconds) / 1_000_000d;
                }
            }
        }
        finally
        {
            try
            {
                if (File.Exists(payloadPath))
                {
                    File.Delete(payloadPath);
                }
            }
            catch
            {
                // Temporary upload data is harmless and will be cleaned by temp cleanup.
            }
        }
        return double.NaN;
    }

    private static async Task<string> RunCurlAsync(
        string curl,
        IEnumerable<string> arguments,
        Action<double> progress,
        CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(curl)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info);
        if (process is null)
        {
            return string.Empty;
        }

        var stopwatch = Stopwatch.StartNew();
        while (!process.HasExited && stopwatch.Elapsed < TimeSpan.FromSeconds(5))
        {
            progress(Math.Clamp(stopwatch.Elapsed.TotalSeconds / 4d * 100d, 0, 100));
            await Task.Delay(100, cancellationToken);
        }
        if (!process.HasExited)
        {
            process.Kill(true);
            return string.Empty;
        }
        return process.StandardOutput.ReadToEnd().Trim();
    }

    private static string? CurlPath()
    {
        foreach (var candidate in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(
                         Environment.SpecialFolder.System), "curl.exe"),
                     Path.Combine(Environment.GetFolderPath(
                         Environment.SpecialFolder.Windows), "System32", "curl.exe")
                 })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    private static async Task<long> UploadWorkerAsync(
        HttpClient client,
        int bytesToSend,
        CancellationToken cancellationToken,
        Action<int> onBytes)
    {
        try
        {
            using var content = new ProgressUploadContent(bytesToSend, onBytes);
            foreach (var url in new[]
                     {
                         "https://speed.cloudflare.com/__up",
                         "http://speedtest.tele2.net/upload.php"
                     })
            {
                try
                {
                    using var response = await client.PostAsync(
                        url, content, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    return bytesToSend;
                }
                catch
                {
                    // Try the next public upload endpoint.
                }
            }
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private static void Report(
        IProgress<SpeedTestProgress>? progress,
        string phase,
        double overall,
        double latency,
        double download,
        double upload,
        string detail)
    {
        progress?.Report(new SpeedTestProgress
        {
            Phase = phase,
            OverallPercent = Math.Clamp(overall, 0, 100),
            LatencyPercent = Math.Clamp(latency, 0, 100),
            DownloadPercent = Math.Clamp(download, 0, 100),
            UploadPercent = Math.Clamp(upload, 0, 100),
            Detail = detail
        });
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(6),
            PooledConnectionLifetime = TimeSpan.FromSeconds(45),
            MaxConnectionsPerServer = 4,
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
            DefaultRequestVersion = new Version(1, 1),
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) KCI/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        return client;
    }

    private sealed class ProgressUploadContent : HttpContent
    {
        private readonly int _bytesToSend;
        private readonly Action<int> _onBytes;

        public ProgressUploadContent(int bytesToSend, Action<int> onBytes)
        {
            _bytesToSend = bytesToSend;
            _onBytes = onBytes;
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                "application/octet-stream");
        }

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            try
            {
                Array.Clear(buffer);
                var remaining = _bytesToSend;
                while (remaining > 0)
                {
                    var count = Math.Min(buffer.Length, remaining);
                    await stream.WriteAsync(buffer.AsMemory(0, count));
                    remaining -= count;
                    _onBytes(count);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _bytesToSend;
            return true;
        }
    }
}
