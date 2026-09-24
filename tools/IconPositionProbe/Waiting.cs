namespace DeskAI.IconProbe;

internal static class Waiting
{
    /// <summary>Asks <paramref name="probe"/> until it answers or <paramref name="timeout"/> passes.</summary>
    internal static async Task<T?> ForAsync<T>(Func<T?> probe, TimeSpan timeout, TimeSpan step, TimeProvider clock)
        where T : class
    {
        var deadline = clock.GetUtcNow() + timeout;
        while (true)
        {
            if (probe() is { } value)
            {
                return value;
            }

            if (clock.GetUtcNow() >= deadline)
            {
                return null;
            }

            await Task.Delay(step).ConfigureAwait(false);
        }
    }
}
