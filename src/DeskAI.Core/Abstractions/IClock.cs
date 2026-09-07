namespace DeskAI.Core.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
