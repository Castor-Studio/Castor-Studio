namespace CastorApplication.Services.Studio;

internal static class RecordingFile
{
    // Deletes the file a cancelled recording produced. The output has stopped by then, but the
    // muxer process, an antivirus or the indexer can hold the file a little longer, so a lock
    // is retried before giving up. A file that was never created counts as deleted.
    public static async Task<bool> TryDeleteAsync(string path, int attempts = 5, TimeSpan? retryDelay = null)
    {
        var delay = retryDelay ?? TimeSpan.FromMilliseconds(200);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (attempt >= attempts) return false;
            }

            await Task.Delay(delay);
        }
    }
}
