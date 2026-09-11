using System.Diagnostics;

namespace DeskAI.Infrastructure.Execution;

/// <summary>
/// A lock only one DeskAI window at a time can hold: a file beside the database, opened so
/// nobody else may open it.
/// </summary>
/// <remarks>
/// <para>
/// A lock inside one process cannot stop a second DeskAI window from tidying the same folder,
/// or from mistaking the first window's tidy, still going, for one that was interrupted. A file
/// opened with no sharing can be held by one process only, is not tied to a thread (so it
/// survives <c>await</c>), and is released by Windows when its process ends — a crash included,
/// which is exactly when the next window must be able to take it.
/// </para>
/// <para>
/// Waiting is bounded. A lock that does not come free is reported, never waited on forever.
/// </para>
/// </remarks>
internal static class RunLockFile
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

    /// <returns>The held lock, released by disposing it; or null if it stayed busy.</returns>
    public static async Task<IDisposable?> TryEnterAsync(string path, TimeSpan wait, CancellationToken cancellationToken)
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (waited.Elapsed < wait)
            {
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return null;
            }
        }
    }
}
