using DeskAI.Core.Files;

namespace DeskAI.Core.Tests;

public sealed class MetadataScanOptionsTests
{
    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    public void Constructor_RejectsUnboundedOrInvalidLimits(int maxDepth, int maxEntries)
    {
        var action = () => new MetadataScanOptions(maxDepth, maxEntries);

        Assert.Throws<ArgumentOutOfRangeException>(action);
    }
}
