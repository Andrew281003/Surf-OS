using System.Net.Http.Headers;
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
    private const int MaximumResponseBytes = 1024 * 1024;
    private const string EndpointVariable = "SURFCLOUD_PUBLISH_URL";
    private const string TokenVariable = "SURFCLOUD_TOKEN";

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

        string token = Environment.GetEnvironmentVariable(TokenVariable) ?? string.Empty;
        try
        {
            using HttpClient client = new() { Timeout = TimeSpan.FromMinutes(2) };
            if (!string.IsNullOrWhiteSpace(token))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

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

            using HttpResponseMessage response = await client.PostAsync(endpoint, form);
            string responseBody = await ReadBoundedResponseAsync(response);
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
            return new PublishResult(false, "SurfCloud publish timed out. No completed upload was confirmed.");
        }
        catch (HttpRequestException ex)
        {
            return new PublishResult(false, $"SurfCloud publish failed: {ex.Message}");
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
