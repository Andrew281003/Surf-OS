using Google.Cloud.Firestore;

internal sealed class PublishReconciler(DriveUploader drive, ILogger<PublishReconciler> logger) : BackgroundService
{
    private readonly Lazy<FirestoreDb> _database = new(() => FirestoreDb.Create(Settings.Required("GOOGLE_CLOUD_PROJECT")));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ReconcileOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning(error, "SurfCloud publish reconciliation pass failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ReconcileOnceAsync(CancellationToken cancellationToken)
    {
        QuerySnapshot stale = await _database.Value.CollectionGroup("operations")
            .WhereLessThan("leaseExpiresAt", Timestamp.GetCurrentTimestamp())
            .Limit(100).GetSnapshotAsync(cancellationToken);
        foreach (DocumentSnapshot snapshot in stale.Documents)
        {
            DocumentReference operation = snapshot.Reference;
            DocumentReference? usage = operation.Parent.Parent;
            if (usage is null) continue;
            string state = ReadString(snapshot, "state");
            if (state is "committed" or "reconciled") continue;
            try
            {
                await _database.Value.RunTransactionAsync(async transaction =>
                {
                    DocumentSnapshot current = await transaction.GetSnapshotAsync(operation);
                    string currentState = ReadString(current, "state");
                    if (currentState is not ("reserved" or "uploading" or "failed")) return;
                    if (!current.TryGetValue("leaseExpiresAt", out Timestamp expiry) ||
                        expiry.ToDateTime() > DateTime.UtcNow) return;
                    if (!current.TryGetValue("reservationReleased", out bool released) || !released)
                    {
                        DocumentSnapshot quota = await transaction.GetSnapshotAsync(usage);
                        long reserved = ReadLong(quota, "reservedBytes");
                        long amount = ReadLong(current, "reservedBytes");
                        transaction.Set(usage, new { reservedBytes = Math.Max(0, reserved - amount) }, SetOptions.MergeAll);
                    }
                    transaction.Set(operation, new { state = "expired" }, SetOptions.MergeAll);
                });
                DocumentSnapshot latest = await operation.GetSnapshotAsync(cancellationToken);
                if (ReadString(latest, "state") != "expired") continue;
                List<string> fileIds = [];
                string packageId = ReadString(latest, "packageDriveFileId");
                string sourceId = ReadString(latest, "sourceDriveFileId");
                if (!string.IsNullOrWhiteSpace(packageId)) fileIds.Add(packageId);
                if (!string.IsNullOrWhiteSpace(sourceId)) fileIds.Add(sourceId);
                IReadOnlyList<string> discovered = await drive.ListByOperationAsync(
                    Settings.Required("SURFCLOUD_DRIVE_FOLDER_ID"), operation.Id, cancellationToken);
                foreach (string fileId in discovered)
                    if (!fileIds.Contains(fileId, StringComparer.Ordinal)) fileIds.Add(fileId);
                bool failed = false;
                await DriveCleanup.CleanupAsync(fileIds, drive.DeleteAsync,
                    (_, error) => { failed = true; logger.LogWarning(error, "Expired publish cleanup failed for {OperationId}", operation.Id); },
                    TimeSpan.FromSeconds(30));
                if (!failed)
                    await operation.SetAsync(new
                    {
                        state = "reconciled", reconciledAt = Timestamp.GetCurrentTimestamp(),
                        leaseExpiresAt = Timestamp.FromDateTime(DateTime.UtcNow.AddYears(100))
                    }, SetOptions.MergeAll, cancellationToken);
                else
                    await operation.SetAsync(new { leaseExpiresAt = Timestamp.FromDateTime(DateTime.UtcNow.AddMinutes(5)) },
                        SetOptions.MergeAll, cancellationToken);
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "Could not reconcile publish operation {OperationId}", operation.Id);
                try
                {
                    DocumentSnapshot current = await operation.GetSnapshotAsync(cancellationToken);
                    if (ReadString(current, "state") == "expired")
                        await operation.SetAsync(new { leaseExpiresAt = Timestamp.FromDateTime(DateTime.UtcNow.AddMinutes(5)) },
                            SetOptions.MergeAll, cancellationToken);
                }
                catch (Exception retryError)
                {
                    logger.LogWarning(retryError, "Could not schedule reconciliation retry for {OperationId}", operation.Id);
                }
            }
        }
    }

    private static long ReadLong(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue(field, out long value) ? value : 0;
    private static string ReadString(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue(field, out string value) ? value : string.Empty;
}
