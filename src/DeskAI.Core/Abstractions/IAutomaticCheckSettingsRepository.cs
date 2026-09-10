using DeskAI.Core.Rules;

namespace DeskAI.Core.Abstractions;

/// <summary>What someone chose about automatic checks, and when one last happened.</summary>
/// <remarks>
/// The last-checked moment lives beside the settings because it is the other half of the
/// same question: <see cref="AutomaticCheckSchedule"/> needs both to answer whether a check
/// is due. Storing it means an app closed overnight knows on the next launch that it is
/// overdue, rather than starting its interval again from zero every time it opens.
/// </remarks>
public interface IAutomaticCheckSettingsRepository
{
    Task<AutomaticCheckSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AutomaticCheckSettings settings, CancellationToken cancellationToken = default);

    Task<DateTimeOffset?> ReadLastCheckedAtUtcAsync(CancellationToken cancellationToken = default);

    Task RecordCheckedAtAsync(DateTimeOffset checkedAtUtc, CancellationToken cancellationToken = default);
}
