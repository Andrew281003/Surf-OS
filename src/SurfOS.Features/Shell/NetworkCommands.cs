using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace SurfOS2;

internal partial class CLI_Engine
{
    internal const int MaximumDownloadBytes = 16 * 1024 * 1024;
    private static void RequireNetwork()
    {
        if (Import.Variables.safeMode || Import.Variables.networkProfile.Equals("Offline", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SurfOS networking is disabled by safe mode or the Offline profile.");
    }

    internal static async Task<byte[]> DownloadHttpAsync(string address)
    {
        RequireNetwork();
        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Use an HTTP(S) URL without embedded credentials.");
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        using HttpClient client = new();
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumDownloadBytes) throw new IOException("Download exceeds 16 MiB.");
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        using MemoryStream output = new();
        byte[] buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, timeout.Token)) != 0)
        {
            if (output.Length + read > MaximumDownloadBytes) throw new IOException("Download exceeds 16 MiB.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static void ExecuteNetwork(string command, string[] args)
    {
        string usage = AdditionalUsage[command].Usage;
        switch (command)
        {
            case "network":
                string action = args.FirstOrDefault() ?? "status";
                Require(args.Length <= 1 && action is "status" or "profiles" or "connect" or "disconnect" || args.Length == 2 && action == "connect" && args[1] is "Private" or "Public", usage);
                if (action == "profiles") { Console.WriteLine("Private\nPublic\nOffline"); return; }
                if (action is "connect" or "disconnect")
                {
                    string profile = action == "disconnect" ? "Offline" : args.ElementAtOrDefault(1) ?? "Private";
                    UpdateOptions(o => o.NetworkProfile = profile);
                    Import.Variables.networkProfile = profile;
                }
                Console.WriteLine($"SurfOS policy: {Import.Variables.networkProfile}; safe mode: {Import.Variables.safeMode}; host link available: {CloudRepositoryManager.IsInternetAvailable()}. Host adapters are unchanged.");
                break;
            case "ip":
                Require(args.Length == 0 || args.Length == 1 && args[0] is "addr" or "route" or "status", usage);
                string mode = args.FirstOrDefault() ?? "addr";
                Console.WriteLine($"Host network data (SurfOS profile: {Import.Variables.networkProfile})");
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    Console.WriteLine($"{nic.Name}: {nic.OperationalStatus}");
                    if (mode == "status") continue;
                    var properties = nic.GetIPProperties();
                    if (mode == "route") foreach (var gateway in properties.GatewayAddresses) Console.WriteLine($"  default via {gateway.Address}");
                    else foreach (var address in properties.UnicastAddresses) Console.WriteLine($"  {address.Address}/{address.PrefixLength}");
                }
                if (mode == "route") Console.WriteLine("Default gateways only; this is not the complete host routing table.");
                break;
            case "netstat":
                Require(args.Length == 0 || args is ["-a"], usage);
                var tcp = IPGlobalProperties.GetIPGlobalProperties();
                Console.WriteLine("Host endpoints (not simulated SurfOS connections):");
                foreach (var connection in tcp.GetActiveTcpConnections()) Console.WriteLine($"TCP {connection.LocalEndPoint} -> {connection.RemoteEndPoint} {connection.State}");
                if (args.Length != 0)
                {
                    foreach (var endpoint in tcp.GetActiveTcpListeners()) Console.WriteLine($"TCP {endpoint} LISTEN");
                    foreach (var endpoint in tcp.GetActiveUdpListeners()) Console.WriteLine($"UDP {endpoint} LISTEN");
                }
                break;
            case "dns":
                Require(args.Length == 2 && args[0] == "lookup", usage); RequireNetwork();
                using (CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10)))
                    foreach (var address in Dns.GetHostAddressesAsync(args[1], timeout.Token).GetAwaiter().GetResult()) Console.WriteLine(address);
                break;
            case "traceroute":
                Require(args.Length == 1, usage); RequireNetwork();
                IPAddress target;
                using (CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10)))
                    target = Dns.GetHostAddressesAsync(args[0], timeout.Token).GetAwaiter().GetResult().FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                        ?? throw new ArgumentException("No IPv4 address found.");
                Console.WriteLine($"Host ICMP route to {target}; 30 hops maximum; '*' means no reply.");
                using (Ping ping = new())
                    for (int ttl = 1; ttl <= 30; ttl++)
                    {
                        var reply = ping.Send(target, 1000, new byte[32], new PingOptions(ttl, true));
                        Console.WriteLine(reply.Status == IPStatus.TimedOut ? $"{ttl,2}  *" : $"{ttl,2}  {reply.Address}  {reply.RoundtripTime}ms  {reply.Status}");
                        if (reply.Status == IPStatus.Success) return;
                        if (reply.Status is not (IPStatus.TtlExpired or IPStatus.TimedOut)) { LastExitCode = 1; return; }
                    }
                ShellError("traceroute: destination did not reply within the hop limit.");
                break;
            case "curl":
            case "wget":
                Require(args.Length == 1 || command == "wget" && args.Length == 3 && args[1] == "-o", usage);
                byte[] data = DownloadHttpAsync(args[0]).GetAwaiter().GetResult();
                if (command == "curl") Console.Write(Encoding.UTF8.GetString(data));
                else
                {
                    string name = args.Length == 3 ? args[2] : Uri.UnescapeDataString(new Uri(args[0]).AbsolutePath.Split('/')[^1]);
                    if (name.Length == 0) name = "index.html";
                    if (args.Length == 1 && !PathSafety.IsSafeFileName(name)) throw new ArgumentException("URL has no safe filename; use -o <file>.");
                    VirtualFileSystem.WriteBytes(name, data);
                    Console.WriteLine($"Downloaded {data.Length} bytes to {name}");
                }
                break;
        }
    }
}
