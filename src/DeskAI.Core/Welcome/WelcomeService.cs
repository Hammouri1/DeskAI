using System.Data.Common;
using DeskAI.Core.Abstractions;

namespace DeskAI.Core.Welcome;

/// <summary>Decides whether the first-run welcome opens, and remembers that it did.</summary>
/// <remarks>
/// <para>
/// Only someone brand new is greeted: DeskAI has never shown the welcome, and it remembers no
/// folder at all. Any remembered folder counts, so a person already using DeskAI is not greeted
/// after an update.
/// </para>
/// <para>
/// It holds the settings store and the folder list, and nothing that can reach a file.
/// </para>
/// </remarks>
public sealed class WelcomeService(IAppSettingsStore settings, IAuthorizedRootRepository roots)
{
    /// <summary>Written the moment the welcome opens. Start fresh removes it.</summary>
    public const string ShownKey = "welcome.shown";

    private readonly IAppSettingsStore _settings = settings;
    private readonly IAuthorizedRootRepository _roots = roots;

    public async Task<bool> ShouldShowAsync(CancellationToken cancellationToken = default)
    {
        if (await _settings.ReadAsync(ShownKey, cancellationToken).ConfigureAwait(false) is not null)
        {
            return false;
        }

        return (await _roots.ListAsync(cancellationToken).ConfigureAwait(false)).Count == 0;
    }

    public Task MarkShownAsync(CancellationToken cancellationToken = default) =>
        _settings.WriteAsync(ShownKey, "yes", cancellationToken);

    /// <summary>True when the welcome should open now; it is then already remembered as shown.</summary>
    /// <remarks>
    /// Remembered before it opens, so Skip, Esc, closing DeskAI, or a crash all count. A welcome
    /// whose showing cannot be written down is not shown at all: it would otherwise open again on
    /// every start, which is worse than never greeting.
    /// </remarks>
    public async Task<bool> ClaimFirstShowAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await ShouldShowAsync(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            await MarkShownAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or DbException
            or IOException
            or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
