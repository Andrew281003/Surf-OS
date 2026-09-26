using Google.Apis.Auth;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Http.Features;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

const long UserQuotaBytes = 15L * 1024 * 1024 * 1024;
const long MaximumUploadBytes = 96L * 1024 * 1024;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MaximumUploadBytes);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = MaximumUploadBytes);
builder.Services.AddHttpClient();
builder.Services.AddSingleton<DriveUploader>();
builder.Services.AddHostedService<PublishReconciler>();

WebApplication app = builder.Build();
string projectId = Settings.Required("GOOGLE_CLOUD_PROJECT");
string driveFolderId = Settings.Required("SURFCLOUD_DRIVE_FOLDER_ID");
string audience = Settings.Required("SURFCLOUD_AUTH_AUDIENCE");
Lazy<FirestoreDb> firestore = new(() => FirestoreDb.Create(projectId));

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/catalog", () =>
{
    string manifestPath = Settings.Required("SURFCLOUD_CATALOG_MANIFEST_PATH");
    string keyId = Settings.Required("SURFCLOUD_CATALOG_KEY_ID");
    if (!long.TryParse(Settings.Required("SURFCLOUD_CATALOG_SEQUENCE"), out long sequence))
        return Results.Problem("Catalog sequence is invalid.");
    FileInfo file = new(manifestPath);
    if (!file.Exists || file.Length is <= 0 or > 1024 * 1024)
        return Results.Problem("Catalog file is unavailable.");
    byte[] bytes = File.ReadAllBytes(manifestPath);
    string signingKeyPath = Settings.Required("SURFCLOUD_CATALOG_SIGNING_KEY_FILE");
    byte[] signingSeed = Convert.FromBase64String(File.ReadAllText(signingKeyPath).Trim());
    try
    {
        SignedCatalog signed = CatalogSigning.Sign(bytes, keyId, sequence, signingSeed);
        return Results.Ok(signed);
    }
    finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(signingSeed); }
});

app.MapGet("/session", async (HttpRequest request) =>
{
    string? subject = await GoogleIdentity.TryGetSubjectAsync(request, audience);
    return subject is null ? Results.Unauthorized() : Results.Ok(new { subject });
});

app.MapPost("/presence", async (HttpRequest request) =>
{
    string? subject = await GoogleIdentity.TryGetSubjectAsync(request, audience);
    if (subject is null) return Results.Unauthorized();
    await firestore.Value.Collection("surfcloudUsers").Document(subject).SetAsync(
        new Dictionary<string, object> { ["lastSeen"] = Timestamp.GetCurrentTimestamp() },
        SetOptions.MergeAll);
    return Results.NoContent();
});

app.MapGet("/operations/{operationId}", async (HttpRequest request, string operationId) =>
{
    string? subject = await GoogleIdentity.TryGetSubjectAsync(request, audience);
    if (subject is null) return Results.Unauthorized();
    if (!Guid.TryParseExact(operationId, "N", out _)) return Results.BadRequest();
    DocumentSnapshot snapshot = await firestore.Value.Collection("surfcloudUsers").Document(subject)
        .Collection("operations").Document(operationId).GetSnapshotAsync();
    return snapshot.Exists
        ? Results.Ok(new { operationId, status = ReadString(snapshot, "state"), packageId = ReadString(snapshot, "packageId") })
        : Results.NotFound();
});

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
    string operationId = request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty;
    if (!Guid.TryParseExact(operationId, "N", out _))
        return Results.BadRequest(new { message = "A GUID Idempotency-Key header is required." });
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

    string packageDigest;
    try
    {
        packageDigest = await PackageArchiveValidator.ValidatePackageAsync(package, manifest, cancellationToken);
        if (source is not null) await PackageArchiveValidator.ValidateSourceAsync(source, cancellationToken);
    }
    catch (Exception ex) when (ex is InvalidDataException or JsonException or InvalidOperationException or OverflowException or NotSupportedException or ArgumentOutOfRangeException)
    {
        return Results.BadRequest(new { message = "Invalid package or source archive." });
    }
    string sourceDigest = string.Empty;
    if (source is not null)
    {
        await using Stream sourceBytes = source.OpenReadStream();
        sourceDigest = Convert.ToHexString(await SHA256.HashDataAsync(sourceBytes, cancellationToken));
    }
    string requestFingerprint = Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(packageDigest + "|" + sourceDigest + "|" + visibility)));

    long incomingBytes = package.Length + (source?.Length ?? 0);
    DocumentReference usage = database.Collection("surfcloudUsers").Document(userId);
    DocumentReference packageRecord = usage.Collection("packages").Document(manifest.Id);
    DocumentReference operation = usage.Collection("operations").Document(operationId);
    Reservation reservation;
    try
    {
        reservation = await database.RunTransactionAsync(async transaction =>
        {
            DocumentSnapshot usageSnapshot = await transaction.GetSnapshotAsync(usage);
            DocumentSnapshot packageSnapshot = await transaction.GetSnapshotAsync(packageRecord);
            DocumentSnapshot previousOperation = await transaction.GetSnapshotAsync(operation);
            if (previousOperation.Exists)
                throw new ExistingOperationException(ReadString(previousOperation, "state"), ReadString(previousOperation, "requestFingerprint") == requestFingerprint);
            long used = ReadLong(usageSnapshot, "usedBytes");
            long reserved = ReadLong(usageSnapshot, "reservedBytes");
            long replacedBytes = ReadLong(packageSnapshot, "totalBytes");
            long projected = checked(used + reserved - replacedBytes + incomingBytes);
            if (projected > UserQuotaBytes)
            {
                throw new QuotaExceededException(used, UserQuotaBytes);
            }
            transaction.Set(usage, new { usedBytes = used, reservedBytes = reserved + incomingBytes, quotaBytes = UserQuotaBytes }, SetOptions.MergeAll);
            transaction.Set(operation, new Dictionary<string, object>
            {
                ["state"] = "reserved", ["packageId"] = manifest.Id, ["packageSha256"] = packageDigest,
                ["requestFingerprint"] = requestFingerprint,
                ["reservedBytes"] = incomingBytes,
                ["leaseExpiresAt"] = Timestamp.FromDateTime(DateTime.UtcNow.AddMinutes(15))
            });
            return new Reservation(used, replacedBytes, incomingBytes);
        });
    }
    catch (QuotaExceededException ex)
    {
        return Results.Json(new { message = "SurfCloud storage quota exceeded.", usedBytes = ex.UsedBytes, quotaBytes = ex.QuotaBytes, remainingBytes = Math.Max(0, ex.QuotaBytes - ex.UsedBytes) }, statusCode: StatusCodes.Status413PayloadTooLarge);
    }
    catch (ExistingOperationException ex)
    {
        if (!ex.SameDigest) return Results.Conflict(new { message = "Idempotency key belongs to a different package." });
        if (ex.State == "committed") return Results.Ok(new { message = "Already published.", operationId, packageId = manifest.Id });
        if (ex.State is "reserved" or "uploading")
            return Results.Accepted($"/operations/{operationId}", new { operationId, status = ex.State });
        return Results.Conflict(new { message = "Previous operation failed. Use a new idempotency key.", operationId });
    }

    UploadedFiles? uploaded = null;
    List<string> createdFiles = [];
    try
    {
        string packageFileId = await drive.UploadAsync(package, driveFolderId, $"{userId}/{manifest.Id}/{Path.GetFileName(package.FileName)}", operationId, cancellationToken);
        createdFiles.Add(packageFileId);
        await operation.SetAsync(new Dictionary<string, object> { ["state"] = "uploading", ["packageDriveFileId"] = packageFileId }, SetOptions.MergeAll, cancellationToken);
        string? sourceFileId = null;
        if (source is not null)
        {
            sourceFileId = await drive.UploadAsync(source, driveFolderId, $"{userId}/{manifest.Id}/{manifest.Id}-source.zip", operationId, cancellationToken);
            createdFiles.Add(sourceFileId);
            await operation.SetAsync(new Dictionary<string, object> { ["sourceDriveFileId"] = sourceFileId }, SetOptions.MergeAll, cancellationToken);
        }
        uploaded = new UploadedFiles(packageFileId, sourceFileId);

        PackageRecord? replaced = null;
        await database.RunTransactionAsync(async transaction =>
        {
            DocumentSnapshot current = await transaction.GetSnapshotAsync(packageRecord);
            DocumentSnapshot currentOperation = await transaction.GetSnapshotAsync(operation);
            if (ReadString(currentOperation, "state") != "uploading" ||
                !currentOperation.TryGetValue("leaseExpiresAt", out Timestamp lease) ||
                lease.ToDateTime() <= DateTime.UtcNow)
                throw new InvalidOperationException("Publishing lease expired.");
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
                ["name"] = manifest.Name,
                ["version"] = manifest.Version,
                ["author"] = manifest.Author,
                ["installPath"] = manifest.InstallPath,
                ["command"] = manifest.Command,
                ["payloadSha256"] = manifest.Sha256,
                ["totalBytes"] = incomingBytes,
                ["packageDriveFileId"] = uploaded.PackageId,
                ["sourceDriveFileId"] = uploaded.SourceId,
                ["visibility"] = visibility,
                ["packageSha256"] = packageDigest,
                ["publisherSubject"] = userId,
                ["publishedAt"] = Timestamp.GetCurrentTimestamp()
            }, SetOptions.Overwrite);
            transaction.Set(usage, new { usedBytes = checked(used - previousBytes + incomingBytes), reservedBytes = Math.Max(0, reserved - incomingBytes), quotaBytes = UserQuotaBytes }, SetOptions.MergeAll);
            transaction.Set(operation, new
            {
                state = "committed", completedAt = Timestamp.GetCurrentTimestamp(),
                leaseExpiresAt = Timestamp.FromDateTime(DateTime.UtcNow.AddYears(100))
            }, SetOptions.MergeAll);
        });

        if (replaced is not null)
        {
            string[] obsolete = [replaced.PackageDriveFileId,
                .. (string.IsNullOrWhiteSpace(replaced.SourceDriveFileId) ? [] : new[] { replaced.SourceDriveFileId })];
            await DriveCleanup.CleanupAsync(obsolete, drive.DeleteAsync,
                (_, cleanupError) => app.Logger.LogWarning(cleanupError,
                    "Published {PackageId}, but could not delete an earlier Drive file", manifest.Id),
                TimeSpan.FromSeconds(30));
        }
        return Results.Ok(new { message = $"Published {manifest.Name} {manifest.Version} {visibility}ly.", operationId, packageId = manifest.Id, usedBytes = reservation.UsedBytes - reservation.ReplacedBytes + reservation.IncomingBytes, quotaBytes = UserQuotaBytes });
    }
    catch (Exception ex)
    {
        try
        {
            DocumentSnapshot status = await operation.GetSnapshotAsync(CancellationToken.None);
            if (ReadString(status, "state") == "committed")
                return Results.Ok(new { message = "Published; acknowledgement was delayed.", operationId, packageId = manifest.Id });
        }
        catch (Exception statusError)
        {
            app.Logger.LogWarning(statusError, "Could not determine operation status for {OperationId}", operationId);
            return Results.Accepted($"/operations/{operationId}", new { message = "Publish outcome is uncertain; check operation status.", operationId });
        }
        await DriveCleanup.CleanupAsync(createdFiles, drive.DeleteAsync,
            (_, error) => app.Logger.LogWarning(error, "Drive cleanup failed after publish error for {PackageId}", manifest.Id),
            TimeSpan.FromSeconds(30));
        bool reservationReleased = false;
        try { await database.RunTransactionAsync(async transaction =>
        {
            DocumentSnapshot current = await transaction.GetSnapshotAsync(usage);
            transaction.Set(usage, new { reservedBytes = Math.Max(0, ReadLong(current, "reservedBytes") - incomingBytes) }, SetOptions.MergeAll);
        }); reservationReleased = true; }
        catch (Exception releaseError)
        {
            app.Logger.LogWarning(releaseError, "Quota reservation release failed for {PackageId}", manifest.Id);
        }
        try { await operation.SetAsync(new { state = "failed", reservationReleased, completedAt = Timestamp.GetCurrentTimestamp() }, SetOptions.MergeAll, CancellationToken.None); }
        catch (Exception stateError) { app.Logger.LogWarning(stateError, "Could not mark operation failed for {OperationId}", operationId); }
        app.Logger.LogError(ex, "Publishing {PackageId} for {UserId} failed", manifest.Id, userId);
        return Results.Problem("SurfCloud upload failed. Check operation status before retrying.", statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();

static long ReadLong(DocumentSnapshot snapshot, string field) => snapshot.Exists && snapshot.TryGetValue(field, out long value) ? value : 0;
static string ReadString(DocumentSnapshot snapshot, string field) => snapshot.TryGetValue(field, out string value) ? value : string.Empty;
static string? ReadNullableString(DocumentSnapshot snapshot, string field) => snapshot.TryGetValue(field, out string value) ? value : null;

internal sealed record PackageManifest(string Id, string Name, string Version, string Author, string Sha256, string InstallPath, string Command);
internal sealed record PackageRecord(string Id, long TotalBytes, string PackageDriveFileId, string? SourceDriveFileId);
internal sealed record Reservation(long UsedBytes, long ReplacedBytes, long IncomingBytes);
internal sealed record UploadedFiles(string PackageId, string? SourceId);
internal sealed class QuotaExceededException(long usedBytes, long quotaBytes) : Exception
{
    public long UsedBytes { get; } = usedBytes;
    public long QuotaBytes { get; } = quotaBytes;
}
internal sealed class ExistingOperationException(string state, bool sameDigest) : Exception
{
    public string State { get; } = state;
    public bool SameDigest { get; } = sameDigest;
}
internal static class PackageId
{
    public static bool IsValid(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 80 && id is not "." and not ".." &&
        !id.EndsWith('.') &&
        System.Text.RegularExpressions.Regex.IsMatch(id, "^[A-Za-z0-9._-]+$") &&
        !System.Text.RegularExpressions.Regex.IsMatch(id, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\\..*)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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
            if (payload.Issuer?.ToString() is not ("accounts.google.com" or "https://accounts.google.com") ||
                !string.Equals(payload.Audience?.ToString(), audience, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(payload.Subject) || payload.ExpirationTimeSeconds is null ||
                payload.ExpirationTimeSeconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;
            return payload.Subject;
        }
        catch (InvalidJwtException) { return null; }
    }
}

internal sealed class DriveUploader(IHttpClientFactory clients)
{
    private readonly HttpClient _client = clients.CreateClient();

    public async Task<string> UploadAsync(IFormFile file, string folderId, string name, string operationId, CancellationToken cancellationToken)
    {
        string accessToken = await DriveToken.GetAsync(_client, cancellationToken);
        using HttpResponseMessage session = await HttpRetry.SendAsync(_client, () =>
        {
            HttpRequestMessage start = new(HttpMethod.Post, "https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable");
            start.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            start.Headers.Add("X-Upload-Content-Type", file.ContentType ?? "application/octet-stream");
            start.Headers.Add("X-Upload-Content-Length", file.Length.ToString());
            start.Content = JsonContent.Create(new { name, parents = new[] { folderId }, appProperties = new Dictionary<string, string> { ["surfcloudOperation"] = operationId } });
            return start;
        }, cancellationToken);
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
        using HttpResponseMessage response = await HttpRetry.SendAsync(_client, () =>
        {
            HttpRequestMessage request = new(HttpMethod.Delete, $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(fileId)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return request;
        }, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<string>> ListByOperationAsync(string folderId, string operationId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(operationId, "N", out _)) throw new ArgumentException("Invalid operation ID.", nameof(operationId));
        string accessToken = await DriveToken.GetAsync(_client, cancellationToken);
        string query = $"'{folderId.Replace("'", "\\'")}' in parents and trashed = false and appProperties has {{ key='surfcloudOperation' and value='{operationId}' }}";
        string url = "https://www.googleapis.com/drive/v3/files?spaces=drive&pageSize=100&fields=nextPageToken,files(id)&q=" + Uri.EscapeDataString(query);
        using HttpResponseMessage response = await HttpRetry.SendAsync(_client, () =>
        {
            HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return request;
        }, cancellationToken);
        response.EnsureSuccessStatusCode();
        using JsonDocument result = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        if (result.RootElement.TryGetProperty("nextPageToken", out _))
            throw new InvalidOperationException("Too many Drive files for one publish operation.");
        return result.RootElement.GetProperty("files").EnumerateArray()
            .Select(file => file.GetProperty("id").GetString())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToArray();
    }
}

internal static class DriveToken
{
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);
    private static string? _accessToken;
    private static DateTimeOffset _expiresAt;

    public static async Task<string> GetAsync(HttpClient client, CancellationToken cancellationToken)
    {
        if (_accessToken is { } cached && _expiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return cached;
        await RefreshLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is { } current && _expiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return current;
        string clientId = Settings.Required("DRIVE_OAUTH_CLIENT_ID");
        string clientSecret = Settings.Required("DRIVE_OAUTH_CLIENT_SECRET");
        string refreshToken = Settings.Required("DRIVE_OAUTH_REFRESH_TOKEN");
        using HttpResponseMessage response = await HttpRetry.SendAsync(client, () =>
        {
            HttpRequestMessage request = new(HttpMethod.Post, "https://oauth2.googleapis.com/token");
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId, ["client_secret"] = clientSecret,
                ["refresh_token"] = refreshToken, ["grant_type"] = "refresh_token"
            });
            return request;
        }, cancellationToken);
        response.EnsureSuccessStatusCode();
        using JsonDocument token = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        string value = token.RootElement.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("Google OAuth did not return an access token.");
        int seconds = token.RootElement.TryGetProperty("expires_in", out JsonElement lifetime) && lifetime.TryGetInt32(out int parsed)
            ? parsed : 3600;
        _accessToken = value;
        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, seconds));
        return value;
        }
        finally { RefreshLock.Release(); }
    }

}
