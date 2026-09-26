using Microsoft.AspNetCore.Http;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class Benchmarks
{
    private const int Operations = 200;
    private static readonly int[] Clients = [1, 10, 50, 100];

    public static async Task RunAsync()
    {
        byte[] payload = Encoding.UTF8.GetBytes("echo fixed benchmark payload\n");
        PackageManifest package = new("bench-app", "Benchmark App", "1.0.0", "Benchmark",
            Convert.ToHexString(SHA256.HashData(payload)), "apps/bench-app/bench-app.surf", "run /apps/bench-app/bench-app.surf");
        byte[] archive = CreatePackage(package, payload);
        byte[] catalog = JsonSerializer.SerializeToUtf8Bytes(Enumerable.Range(0, 100)
            .Select(index => new { Id = $"package-{index:000}", Name = $"Package {index:000}" }).ToArray());
        byte[] download = new byte[64 * 1024];
        new Random(20260925).NextBytes(download);
        using HttpClient client = new(new FixedHandler(download));
        Console.WriteLine("workload,clients,operations,p50_ms,p95_ms,p99_ms,throughput_ops_s,error_rate_pct,cpu_ms,peak_working_set_bytes,mock_http_calls,mock_drive_uploads");
        foreach (int concurrency in Clients)
        {
            await Measure("package_validation", concurrency, async () =>
            {
                FormFile file = new(new MemoryStream(archive), 0, archive.Length, "package", "bench.surfpkg");
                await PackageArchiveValidator.ValidatePackageAsync(file, package, CancellationToken.None);
                return (0, 0);
            });
            await Measure("listing", concurrency, () =>
            {
                using JsonDocument parsed = JsonDocument.Parse(catalog);
                _ = parsed.RootElement.GetArrayLength();
                return Task.FromResult((0, 0));
            });
            await Measure("search", concurrency, () =>
            {
                using JsonDocument parsed = JsonDocument.Parse(catalog);
                _ = parsed.RootElement.EnumerateArray().Count(item => item.GetProperty("Name").GetString()!.Contains("50"));
                return Task.FromResult((0, 0));
            });
            await Measure("mock_download", concurrency, async () =>
            {
                using HttpResponseMessage response = await client.GetAsync("https://mock.invalid/fixed", HttpCompletionOption.ResponseHeadersRead);
                await using Stream stream = await response.Content.ReadAsStreamAsync();
                _ = await SHA256.HashDataAsync(stream);
                return (1, 0);
            });
            await Measure("publish_validation_mock_drive", concurrency, async () =>
            {
                FormFile file = new(new MemoryStream(archive), 0, archive.Length, "package", "bench.surfpkg");
                await PackageArchiveValidator.ValidatePackageAsync(file, package, CancellationToken.None);
                await Task.Yield();
                return (0, 1);
            });
        }
    }

    private static async Task Measure(string workload, int clients, Func<Task<(int Http, int Drive)>> action)
    {
        long[] samples = new long[Operations];
        int errors = 0, http = 0, drive = 0;
        Process process = Process.GetCurrentProcess();
        TimeSpan cpuStart = process.TotalProcessorTime;
        Stopwatch total = Stopwatch.StartNew();
        Task[] workers = Enumerable.Range(0, clients).Select(worker => Task.Run(async () =>
        {
            for (int index = worker; index < Operations; index += clients)
            {
                long start = Stopwatch.GetTimestamp();
                try
                {
                    (int httpCalls, int driveCalls) = await action();
                    Interlocked.Add(ref http, httpCalls);
                    Interlocked.Add(ref drive, driveCalls);
                }
                catch { Interlocked.Increment(ref errors); }
                samples[index] = Stopwatch.GetTimestamp() - start;
            }
        })).ToArray();
        await Task.WhenAll(workers);
        total.Stop();
        process.Refresh();
        Array.Sort(samples);
        double Ms(int index) => samples[index] * 1000.0 / Stopwatch.Frequency;
        Console.WriteLine(string.Join(',', workload, clients, Operations,
            Ms(PercentileIndex(.50)).ToString("F4"), Ms(PercentileIndex(.95)).ToString("F4"),
            Ms(PercentileIndex(.99)).ToString("F4"), (Operations / total.Elapsed.TotalSeconds).ToString("F1"),
            (errors * 100.0 / Operations).ToString("F2"),
            (process.TotalProcessorTime - cpuStart).TotalMilliseconds.ToString("F1"),
            process.PeakWorkingSet64, http, drive));
    }

    private static int PercentileIndex(double percentile) =>
        Math.Clamp((int)Math.Ceiling(Operations * percentile) - 1, 0, Operations - 1);

    private static byte[] CreatePackage(PackageManifest manifest, byte[] payload)
    {
        using MemoryStream output = new();
        using (ZipArchive zip = new(output, ZipArchiveMode.Create, true))
        {
            using (Stream stream = zip.CreateEntry("manifest.json").Open())
                JsonSerializer.Serialize(stream, manifest);
            using Stream target = zip.CreateEntry("payload/bench-app.surf").Open();
            target.Write(payload);
        }
        return output.ToArray();
    }

    private sealed class FixedHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }
}
