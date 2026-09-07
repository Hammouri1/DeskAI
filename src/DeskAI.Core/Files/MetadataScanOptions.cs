namespace DeskAI.Core.Files;

public sealed record MetadataScanOptions
{
    public static MetadataScanOptions Default { get; } = new(maxDepth: 64, maxEntries: 100_000);

    public MetadataScanOptions(int maxDepth, int maxEntries)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 1);
        MaxDepth = maxDepth;
        MaxEntries = maxEntries;
    }

    public int MaxDepth { get; }

    public int MaxEntries { get; }
}
