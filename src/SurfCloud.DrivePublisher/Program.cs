using Google.Apis.Auth;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Http.Features;
using System.Net.Http.Headers;
using System.Text.Json;

const long UserQuotaBytes = 15L * 1024 * 1024 * 1024;
const long MaximumUploadBytes = 96L * 1024 * 1024;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MaximumUploadBytes);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = MaximumUploadBytes);
builder.Services.AddHttpClient();
builder.Services.AddSingleton<DriveUploader>();

WebApplication app = builder.Build();
string projectId = Settings.Required("GOOGLE_CLOUD_PROJECT");
string driveFolderId = Settings.Required("SURFCLOUD_DRIVE_FOLDER_ID");
string audience = Settings.Required("SURFCLOUD_AUTH_AUDIENCE");
Lazy<FirestoreDb> firestore = new(() => FirestoreDb.Create(projectId));

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/publish", async (
    HttpRequest request,
    DriveUploader drive,
    CancellationToken cancellationToken) =>
{
    string? userId = await GoogleIdentity.TryGetSubjectAsync(request, audience);
    if (userId is null)
    {
        return Results.Unauthorized();
    }
    FirestoreDb database = firestore.Value;

    IFormCollection form = await request.ReadFormAsync(cancellationToken);
    IFormFile? package = form.Files.GetFile("package");
    IFormFile? source = form.Files.GetFile("source");
    string? manifestJson = form["manifest"].FirstOrDefault();
    string visibility = form["visibility"].FirstOrDefault()?.ToLowerInvariant() ?? "private";

    if (package is null || package.Length == 0 || string.IsNullOrWhiteSpace(manifestJson))
    {
        return Results.BadRequest(new { message = "Fields package and manifest are required." });
    }
    if (visibility is not ("private" or "public"))
    {
        return Results.BadRequest(new { message = "visibility must be private or public." });
    }
    if (package.Length + (source?.Length ?? 0) > MaximumUploadBytes)
    {
        return Results.BadRequest(new { message = "Upload exceeds the 96 MiB SurfCloud request limit." });
    }

    PackageManifest? manifest;
    try { manifest = JsonSerializer.Deserialize<PackageManifest>(manifestJson); }
    catch (JsonException) { return Results.BadRequest(new { message = "manifest is not valid JSON." }); }
    if (manifest is null || !PackageId.IsValid(manifest.Id))
    {
        return Results.BadRequest(new { message = "manifest has an invalid package ID." });
    }

    long incomingBytes = package.Length + (source?.Length ?? 0);
    string uploadId = Guid.NewGuid().ToString("N");
    DocumentReference usage = database.Collection("surfcloudUsers").Document(userId);
    DocumentReference packageRecord = usage.Collection("packages").Document(manifest.Id);
    Reservation reservation;
    try
    {
        reservation = await database.RunTransactionAsync(async transaction =>
        {
            DocumentSnapshot usageSnapshot = await transaction.GetSnapshotAsync(usage);
            DocumentSnapshot packageSnapshot = await transaction.GetSnapshotAsync(packageRecord);
            long used = ReadLong(usageSnapshot, "usedBytes");
            long reserved = ReadLong(usageSnapshot, "reservedBytes");
            long replacedBytes = ReadLong(packageSnapshot, "totalBytes");
            long projected = checked(used + reserved - replacedBytes + incomingBytes);
            if (projected > UserQuotaBytes)
            {
                throw new QuotaExceededException(used, UserQuotaBytes);
            }
            transaction.Set(usage, new { usedBytes = used, reservedBytes = reserved + incomingBytes, quotaBytes = UserQuotaBytes }, SetOptions.MergeAll);
            return new Reservation(used, replacedBytes, incomingBytes);
        });
    }
    catch (QuotaExceededException ex)
    {
        return Results.Json(new { message = "SurfCloud storage quota exceeded.", usedBytes = ex.UsedBytes, quotaBytes = ex.QuotaBytes, remainingBytes = Math.Max(0, ex.QuotaBytes - ex.UsedBytes) }, statusCode: StatusCodes.Status413PayloadTooLarge);
    }

    UploadedFiles? uploaded = null;
    try
    {
        uploaded = new UploadedFiles(
            await drive.UploadAsync(package, driveFolderId, $"{userId}/{manifest.Id}/{Path.GetFileName(package.FileName)}", cancellationToken),
            source is null ? null : await drive.UploadAsync(source, driveFolderId, $"{userId}/{manifest.Id}/{manifest.Id}-source.zip", cancellationToken));

        PackageRecord? replaced = null;
        await database.RunTransactionAsync(async transaction =>
        {
            DocumentSnapshot current = await transaction.GetSnapshotAsync(packageRecord);
            replaced = current.Exists
                ? new PackageRecord(
                    manifest.Id,
                    ReadLong(current, "totalBytes"),
                    ReadString(current, "packageDriveFileId"),
                    ReadNullableString(current, "sourceDriveFileId"))
                : null;
            DocumentSnapshot currentUsage = await transaction.GetSnapshotAsync(usage);
            long used = ReadLong(currentUsage, "usedBytes");
            long reserved = ReadLong(currentUsage, "reservedBytes");
            long previousBytes = ReadLong(current, "totalBytes");
            transaction.Set(packageRecord, new Dictionary<string, object?>
            {
                ["id"] = manifest.Id,
                ["totalBytes"] = incomingBytes,
                ["packageDriveFileId"] = uploaded.PackageId,
                ["sourceDriveFileId"] = uploaded.SourceId,
                ["visibility"] = visibility,
                ["publishedAt"] = Timestamp.GetCurrentTimestamp()
            }, SetOptions.Overwrite);
            transaction.Set(usage, new { usedBytes = checked(used - previousBytes + incomingBytes), reservedBytes = Math.Max(0, reserved - incomingBytes), quotaBytes = UserQuotaBytes }, SetOptions.MergeAll);
        });

        if (replaced is not null)
        {
            try
            {
                await drive.DeleteAsync(replaced.PackageDriveFileId, cancellationToken);
                if (!string.IsNullOrWhiteSpace(replaced.SourceDriveFileId)) await drive.DeleteAsync(replaced.SourceDriveFileId, cancellationToken);
            }
            catch (Exception cleanupError)
            {
                app.Logger.LogWarning(cleanupError, "Published {PackageId}, but could not delete an earlier Drive file", manifest.Id);
            }
        }
        return Results.Ok(new { message = $"Published {manifest.Name} {manifest.Version} {visibility}ly.", packageId = manifest.Id, usedBytes = reservation.UsedBytes - reservation.ReplacedBytes + reservation.IncomingBytes, quotaBytes = UserQuotaBytes });
    }
    catch (Exception ex)
    {
        if (uploaded is not null)
        {
            await drive.DeleteAsync(uploaded.PackageId, CancellationToken.None);
            if (uploaded.SourceId is not null) await drive.DeleteAsync(uploaded.SourceId, CancellationToken.None);
        }
        await database.RunTransactionAsync(async transaction =>
        {
            DocumentSnapshot current = await transaction.GetSnapshotAsync(usage);
            transaction.Set(usage, new { reservedBytes = Math.Max(0, ReadLong(current, "reservedBytes") - incomingBytes) }, SetOptions.MergeAll);
        });
        app.Logger.LogError(ex, "Publishing {PackageId} for {UserId} failed", manifest.Id, userId);
        return Results.Problem("SurfCloud upload failed. Storage quota was not consumed.", statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();

static long ReadLong(DocumentSnapshot snapshot, string field) => snapshot.Exists && snapshot.TryGetValue(field, out long value) ? value : 0;
static string ReadString(DocumentSnapshot snapshot, string field) => snapshot.TryGetValue(field, out string value) ? value : string.Empty;
static string? ReadNullableString(DocumentSnapshot snapshot, string field) => snapshot.TryGetValue(field, out string value) ? value : null;

internal sealed record PackageManifest(string Id, string Name, string Version);
internal sealed record PackageRecord(string Id, long TotalBytes, string PackageDriveFileId, string? SourceDriveFileId);
internal sealed record Reservation(long UsedBytes, long ReplacedBytes, long IncomingBytes);
internal sealed record UploadedFiles(string PackageId, string? SourceId);
internal sealed class QuotaExceededException(long usedBytes, long quotaBytes) : Exception
{
    public long UsedBytes { get; } = usedBytes;
    public long QuotaBytes { get; } = quotaBytes;
}
internal static class PackageId
{
    public static bool IsValid(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 80 && id.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.');
}

internal static class Settings
{
    public static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value : throw new InvalidOperationException($"Required environment variable {name} is not configured.");
}

internal static class GoogleIdentity
{
    public static async Task<string?> TryGetSubjectAsync(HttpRequest request, string audience)
    {
        if (!AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out AuthenticationHeaderValue? header) ||
            !header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(header.Parameter)) return null;
        try
        {
            GoogleJsonWebSignature.Payload payload = await GoogleJsonWebSignature.ValidateAsync(header.Parameter, new GoogleJsonWebSignature.ValidationSettings { Audience = [audience] });
            return payload.Subject;
        }
        catch (InvalidJwtException) { return null; }
    }
}

internal sealed class DriveUploader(IHttpClientFactory clients)
{
    private readonly HttpClient _client = clients.CreateClient();

    public async Task<string> UploadAsync(IFormFile file, string folderId, string name, CancellationToken cancellationToken)
    {
        string accessToken = await DriveToken.GetAsync(_client, cancellationToken);
        using HttpRequestMessage start = new(HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable");
        start.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        start.Headers.Add("X-Upload-Content-Type", file.ContentType ?? "application/octet-stream");
        start.Headers.Add("X-Upload-Content-Length", file.Length.ToString());
        start.Content = JsonContent.Create(new { name, parents = new[] { folderId } });
        using HttpResponseMessage session = await _client.SendAsync(start, cancellationToken);
        session.EnsureSuccessStatusCode();
        Uri endpoint = session.Headers.Location ?? throw new InvalidOperationException("Google Drive did not return an upload session.");

        await using Stream source = file.OpenReadStream();
        using StreamContent body = new(source);
        body.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType ?? "application/octet-stream");
        body.Headers.ContentLength = file.Length;
        using HttpRequestMessage upload = new(HttpMethod.Put, endpoint) { Content = body };
        upload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        upload.Headers.Add("Content-Range", $"bytes 0-{file.Length - 1}/{file.Length}");
        using HttpResponseMessage completed = await _client.SendAsync(upload, cancellationToken);
        completed.EnsureSuccessStatusCode();
        using JsonDocument result = JsonDocument.Parse(await completed.Content.ReadAsStreamAsync(cancellationToken));
        return result.RootElement.GetProperty("id").GetString() ?? throw new InvalidOperationException("Google Drive did not return a file ID.");
    }

    public async Task DeleteAsync(string? fileId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileId)) return;
        string accessToken = await DriveToken.GetAsync(_client, cancellationToken);
        using HttpRequestMessage request = new(HttpMethod.Delete, $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(fileId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage response = await _client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
    }
}

internal static class DriveToken
{
    public static async Task<string> GetAsync(HttpClient client, CancellationToken cancellationToken)
    {
        string clientId = Settings.Required("DRIVE_OAUTH_CLIENT_ID");
        string clientSecret = Settings.Required("DRIVE_OAUTH_CLIENT_SECRET");
        string refreshToken = Settings.Required("DRIVE_OAUTH_REFRESH_TOKEN");
        using FormUrlEncodedContent body = new(new Dictionary<string, string>
        {
            ["client_id"] = clientId, ["client_secret"] = clientSecret, ["refresh_token"] = refreshToken, ["grant_type"] = "refresh_token"
        });
        using HttpResponseMessage response = await client.PostAsync("https://oauth2.googleapis.com/token", body, cancellationToken);
        response.EnsureSuccessStatusCode();
        using JsonDocument token = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        return token.RootElement.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("Google OAuth did not return an access token.");
    }
}
