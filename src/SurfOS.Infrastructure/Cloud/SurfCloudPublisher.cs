using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SurfOS2;

internal enum PackageVisibility
{
    Private,
    Public
}

internal sealed record PublishResult(bool Success, string Message, string? PublicationUrl = null);

/// <summary>
/// Uploads a validated package to the operator-configured SurfCloud publishing API.
/// Credentials stay outside the app and are sent only to an HTTPS endpoint.
/// </summary>
internal static class SurfCloudPublisher
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(2) };
    private const int MaximumResponseBytes = 1024 * 1024;
    private const string EndpointVariable = "SURFCLOUD_PUBLISH_URL";

    public static async Task<PublishResult> PublishAsync(
        byte[] packageBytes,
        string packageFileName,
        StorePackageManifest manifest,
        PackageVisibility visibility,
        byte[]? sourceSnapshot = null)
    {
        string endpointValue = Environment.GetEnvironmentVariable(EndpointVariable) ?? string.Empty;
        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out Uri? endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttps && !endpoint.IsLoopback))
        {
            return new PublishResult(
                false,
                $"SurfCloud publishing is not configured. Set {EndpointVariable} to an HTTPS upload endpoint.");
        }

        string? token = Cloud_Manager.GetVerifiedSessionToken();
        if (string.IsNullOrWhiteSpace(token))
            return new PublishResult(false, "Sign in to SurfCloud before publishing.");
        using IncrementalHash operationHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        operationHash.AppendData(packageBytes);
        if (sourceSnapshot is not null) operationHash.AppendData(sourceSnapshot);
        operationHash.AppendData(Encoding.UTF8.GetBytes(visibility.ToString().ToLowerInvariant()));
        string operationId = Convert.ToHexString(operationHash.GetHashAndReset())[..32].ToLowerInvariant();
        try
        {
            using MultipartFormDataContent form = new();
            form.Add(new StringContent(
                visibility.ToString().ToLowerInvariant(),
                Encoding.UTF8), "visibility");
            form.Add(new StringContent(
                JsonSerializer.Serialize(manifest),
                Encoding.UTF8,
                "application/json"), "manifest");

            ByteArrayContent packageContent = new(packageBytes);
            packageContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.surfos.package");
            form.Add(packageContent, "package", Path.GetFileName(packageFileName));

            if (sourceSnapshot is { Length: > 0 })
            {
                ByteArrayContent sourceContent = new(sourceSnapshot);
                sourceContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                form.Add(sourceContent, "source", $"{manifest.Id}-source.zip");
            }

            using HttpRequestMessage request = new(HttpMethod.Post, endpoint) { Content = form };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("Idempotency-Key", operationId);
            using HttpResponseMessage response = await Client.SendAsync(request);
            string responseBody = await ReadBoundedResponseAsync(response);
            if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
                return new PublishResult(false, $"SurfCloud publish is pending. Operation ID: {operationId}.");
            if (!response.IsSuccessStatusCode)
            {
                string detail = TryReadMessage(responseBody) ?? response.ReasonPhrase ?? "upload rejected";
                return new PublishResult(false, $"SurfCloud publish failed ({(int)response.StatusCode}): {detail}");
            }

            string? publicationUrl = TryReadUrl(responseBody);
            string message = visibility == PackageVisibility.Private
                ? $"Published {manifest.Name} {manifest.Version} privately."
                : $"Published {manifest.Name} {manifest.Version} publicly.";
            return new PublishResult(true, message, publicationUrl);
        }
        catch (TaskCanceledException)
        {
            string status = await GetOperationStatusAsync(endpoint, operationId, token);
            if (status == "committed")
                return new PublishResult(true, $"SurfCloud publish completed. Operation ID: {operationId}.");
            return new PublishResult(false, $"SurfCloud publish outcome is {status}. Operation ID: {operationId}. Repeating the same request uses the same idempotency key.");
        }
        catch (HttpRequestException ex)
        {
            return new PublishResult(false, $"SurfCloud publish failed: {ex.Message}");
        }
    }

    private static async Task<string> GetOperationStatusAsync(Uri endpoint, string operationId, string token)
    {
        try
        {
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri(endpoint, "/operations/" + operationId));
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage response = await Client.SendAsync(request, deadline.Token);
            if (!response.IsSuccessStatusCode) return "unconfirmed";
            string body = await ReadBoundedResponseAsync(response);
            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("status", out JsonElement value)
                ? value.GetString() ?? "unconfirmed" : "unconfirmed";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return "unconfirmed";
        }
    }

    private static async Task<string> ReadBoundedResponseAsync(HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
        {
            return string.Empty;
        }

        await using Stream source = await response.Content.ReadAsStreamAsync();
        using MemoryStream destination = new();
        byte[] buffer = new byte[8192];
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            if (destination.Length + read > MaximumResponseBytes) return string.Empty;
            destination.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(destination.ToArray());
    }

    private static string? TryReadMessage(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("message", out JsonElement value)
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return string.IsNullOrWhiteSpace(json) ? null : json.Trim();
        }
    }

    private static string? TryReadUrl(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("url", out JsonElement value))
            {
                return null;
            }
            string? url = value.GetString();
            return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps
                ? uri.ToString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
