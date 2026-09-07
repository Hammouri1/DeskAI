using DeskAI.Core.Abstractions;

namespace DeskAI.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
