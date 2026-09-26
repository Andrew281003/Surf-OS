using System.Net.Http.Headers;
using System.Text.Json;

namespace SurfOS2;

/// <summary>Unprivileged client for the narrow SurfCloud presence API.</summary>
internal static class Cloud_Manager
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly SemaphoreSlim SessionLock = new(1, 1);
    private static readonly object SessionStateLock = new();
    private static string? _token;
    private static string? _subject;
    private static bool _signedOut;
    private static int _sessionGeneration;

    public static string? VerifiedSubject => _subject;
    public static bool SignedOut => _signedOut;

    internal static string? GetVerifiedSessionToken()
    {
        lock (SessionStateLock) return _subject is null ? null : _token;
    }

    public static async Task<bool> SignInAsync(string idToken, CancellationToken cancellationToken)
    {
        if (!TryGetEndpoint(out Uri? endpoint) || string.IsNullOrWhiteSpace(idToken)) return false;
        await SessionLock.WaitAsync(cancellationToken);
        try
        {
            int generation;
            lock (SessionStateLock)
            {
                generation = _sessionGeneration;
                ClearSession();
            }
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri(endpoint!, "session"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
            using HttpResponseMessage response = await Client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return false;
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument body = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            string? subject = body.RootElement.GetProperty("subject").GetString();
            if (string.IsNullOrWhiteSpace(subject)) return false;
            lock (SessionStateLock)
            {
                if (_sessionGeneration != generation) return false;
                _token = idToken;
                _subject = subject;
                _signedOut = false;
            }
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or KeyNotFoundException or InvalidOperationException)
        {
            lock (SessionStateLock) ClearSession();
            return false;
        }
        finally { SessionLock.Release(); }
    }

    public static void SignOut()
    {
        lock (SessionStateLock)
        {
            _sessionGeneration++;
            _signedOut = true;
            ClearSession();
        }
    }

    private static void ClearSession()
    {
        _token = null;
        _subject = null;
        Import.Variables.surfCloudSignedIn = false;
        Import.Variables.surfCloudAccount = string.Empty;
    }

    public static async Task RegisterUserHeartbeatAsync(CancellationToken cancellationToken)
    {
        string? token = _token;
        if (token is null || _subject is null || !TryGetEndpoint(out Uri? endpoint)) return;
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, new Uri(endpoint!, "presence"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage response = await Client.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) SignOut();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Offline presence is omitted; the server never receives a locally asserted identity.
        }
    }

    private static bool TryGetEndpoint(out Uri? endpoint)
    {
        string? configured = Environment.GetEnvironmentVariable("SURFCLOUD_API_URL");
        if (Uri.TryCreate(configured, UriKind.Absolute, out endpoint) &&
            (endpoint.Scheme == Uri.UriSchemeHttps || endpoint.IsLoopback))
        {
            endpoint = new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/");
            return true;
        }
        endpoint = null;
        return false;
    }
}
