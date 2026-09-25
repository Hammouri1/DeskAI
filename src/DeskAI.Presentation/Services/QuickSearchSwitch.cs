using DeskAI.Core.QuickSearch;

namespace DeskAI.App.Services;

/// <summary>
/// Turns quick search on or off everywhere at once: the stored choice, the shortcut, and the icon.
/// </summary>
/// <remarks>
/// One singleton, so the card on My workspace, startup, and Start fresh cannot leave the
/// shortcut listening while the icon says otherwise.
/// </remarks>
public sealed class QuickSearchSwitch(QuickSearchSettingsService settings, IQuickSearchHotKey hotKey, BackgroundPresenceController presence)
{
    private readonly QuickSearchSettingsService _settings = settings;
    private readonly IQuickSearchHotKey _hotKey = hotKey;
    private readonly BackgroundPresenceController _presence = presence;

    /// <summary>At startup and after Start fresh: do what is stored.</summary>
    public async Task<HotKeyState> ApplyStoredAsync() =>
        Apply((await _settings.LoadAsync().ConfigureAwait(true)).IsOn);

    public async Task<HotKeyState> SetOnAsync(bool on)
    {
        await _settings.SetOnAsync(on).ConfigureAwait(true);
        return Apply(on);
    }

    private HotKeyState Apply(bool on)
    {
        var state = _hotKey.Listen(on);
        _presence.SetQuickSearch(on);
        return state;
    }
}
