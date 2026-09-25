using System.Data.Common;
using DeskAI.Core.Abstractions;

namespace DeskAI.Core.QuickSearch;

/// <summary>Whether quick search listens for its shortcut, which buddy shows, and whether the tip was closed.</summary>
public sealed record QuickSearchSettings(bool IsOn, SearchBuddy Buddy, bool TipDismissed)
{
    public static QuickSearchSettings Default { get; } = new(true, SearchBuddy.Sparky, false);
}

/// <summary>
/// Reads and writes quick search's three small values in the app settings store.
/// </summary>
/// <remarks>
/// Holds the settings store and nothing else. A store that cannot be read gives the defaults:
/// quick search on is the owner's chosen default, so failing towards it changes nothing a person
/// chose, and the switch on My workspace still turns it off.
/// </remarks>
public sealed class QuickSearchSettingsService(IAppSettingsStore store)
{
    public const string OnKey = "quicksearch.on";
    public const string BuddyKey = "quicksearch.buddy";
    public const string TipKey = "quicksearch.tip.dismissed";

    private readonly IAppSettingsStore _store = store;

    /// <summary>Everything Start fresh must forget.</summary>
    public static IReadOnlyList<string> Keys { get; } = [OnKey, BuddyKey, TipKey];

    public async Task<QuickSearchSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var on = await _store.ReadAsync(OnKey, cancellationToken).ConfigureAwait(false);
            var buddy = await _store.ReadAsync(BuddyKey, cancellationToken).ConfigureAwait(false);
            var tip = await _store.ReadAsync(TipKey, cancellationToken).ConfigureAwait(false);
            return new QuickSearchSettings(on != "no", ReadBuddy(buddy), tip == "yes");
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or DbException
            or IOException
            or UnauthorizedAccessException)
        {
            return QuickSearchSettings.Default;
        }
    }

    public Task SetOnAsync(bool isOn, CancellationToken cancellationToken = default) =>
        _store.WriteAsync(OnKey, isOn ? "yes" : "no", cancellationToken);

    public Task SetBuddyAsync(SearchBuddy buddy, CancellationToken cancellationToken = default) =>
        _store.WriteAsync(BuddyKey, buddy.ToString(), cancellationToken);

    public Task DismissTipAsync(CancellationToken cancellationToken = default) =>
        _store.WriteAsync(TipKey, "yes", cancellationToken);

    /// <summary>Only an exact buddy name counts; anything else, numbers included, is Sparky.</summary>
    private static SearchBuddy ReadBuddy(string? stored) =>
        Enum.GetValues<SearchBuddy>().FirstOrDefault(buddy => string.Equals(buddy.ToString(), stored, StringComparison.Ordinal));
}
