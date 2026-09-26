internal static class DriveCleanup
{
    public static async Task CleanupAsync(
        IEnumerable<string> fileIds,
        Func<string, CancellationToken, Task> delete,
        Action<string, Exception> log,
        TimeSpan timeout)
    {
        using CancellationTokenSource deadline = new(timeout);
        foreach (string fileId in fileIds)
        {
            try { await delete(fileId, deadline.Token); }
            catch (Exception error) { log(fileId, error); }
        }
    }
}
