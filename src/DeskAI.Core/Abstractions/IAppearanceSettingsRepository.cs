using DeskAI.Core.Appearance;

namespace DeskAI.Core.Abstractions;

/// <summary>Remembers how DeskAI's own window should look. A choice about DeskAI, never about Windows.</summary>
public interface IAppearanceSettingsRepository
{
    Task<AppearanceSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppearanceSettings settings, CancellationToken cancellationToken = default);
}
