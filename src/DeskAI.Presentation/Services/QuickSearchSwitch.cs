using DeskAI.Core.QuickSearch;

namespace DeskAI.App.Services;

/// <summary>
/// Turns quick search on or off everywhere at once, and chooses which shortcut it listens for:
/// the stored choice, the shortcut, and the icon.
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
    public async Task<HotKeyState> ApplyStoredAsync()
    {
        var stored = await _settings.LoadAsync().ConfigureAwait(true);
        return Apply(stored.IsOn, stored.Shortcut);
    }

    public async Task<HotKeyState> SetOnAsync(bool on)
    {
        await _settings.SetOnAsync(on).ConfigureAwait(true);
        return Apply(on, (await _settings.LoadAsync().ConfigureAwait(true)).Shortcut);
    }

    /// <summary>
    /// Remembers the shortcut and, while quick search is on, listens for it at once. A refused
    /// shortcut is not replaced by the old one: nothing listens until another is picked.
    /// </summary>
    public async Task<HotKeyState> SetShortcutAsync(QuickSearchShortcut shortcut)
    {
        await _settings.SetShortcutAsync(shortcut).ConfigureAwait(true);
        return Apply((await _settings.LoadAsync().ConfigureAwait(true)).IsOn, shortcut);
    }

    private HotKeyState Apply(bool on, QuickSearchShortcut shortcut)
    {
        var state = _hotKey.Listen(on, shortcut);
        _presence.SetQuickSearch(on, shortcut);
        return state;
    }
}
