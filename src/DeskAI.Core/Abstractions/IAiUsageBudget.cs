namespace DeskAI.Core.Abstractions;

public interface IAiUsageBudget
{
    Task<bool> TryReserveRequestAsync(
        string providerId,
        int dailyLimit,
        DateOnly utcDate,
        CancellationToken cancellationToken = default);

    Task<int> GetRequestCountAsync(
        string providerId,
        DateOnly utcDate,
        CancellationToken cancellationToken = default);
}
