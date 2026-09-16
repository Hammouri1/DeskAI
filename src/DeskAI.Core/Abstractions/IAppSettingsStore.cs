namespace DeskAI.Core.Abstractions;

/// <summary>
/// Small named values DeskAI keeps between runs, such as the wallpaper it replaced.
/// </summary>
/// <remarks>
/// A key/value store, not a place for anything that deserves its own table and shape. Keys
/// are chosen by the one service that owns them; nothing lists keys.
/// </remarks>
public interface IAppSettingsStore
{
    Task<string?> ReadAsync(string key, CancellationToken cancellationToken = default);

    Task WriteAsync(string key, string value, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
