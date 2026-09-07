using DeskAI.Core.Ai;

namespace DeskAI.Core.Abstractions;

public interface IAiSettingsRepository
{
    Task<AiSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AiSettings settings, CancellationToken cancellationToken = default);
}
