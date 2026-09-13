using DeskAI.Core.Abstractions;
using DeskAI.Core.Rules;

namespace DeskAI.App.Services;

/// <summary>
/// The one thing that owns DeskAI's icon near the clock.
/// </summary>
/// <remarks>
/// <para>
/// A singleton on purpose. View models are created fresh for each page someone opens, so a
/// view model subscribing to the icon's events would add another handler every visit, and one
/// click on "Pause checking" would toggle it as many times as the page had been opened.
/// Having a single owner makes "the page and the icon never disagree" structural rather than
/// something each caller has to be careful about.
/// </para>
/// <para>
/// It decides nothing about what a check may do. It reads and writes the same settings the
/// Automatic tasks page reads and writes, stops a check that is running when someone pauses,
/// and hands the icon the words <see cref="BackgroundCheckingChoice"/> computed. See ADR 0025.
/// </para>
/// </remarks>
public sealed class BackgroundPresenceController : IDisposable
{
    private readonly IBackgroundPresence _presence;
    private readonly IAutomaticCheckSettingsRepository _settings;
    private readonly AutomaticCheckCoordinator _checks;
    private bool _disposed;

    public BackgroundPresenceController(
        IBackgroundPresence presence,
        IAutomaticCheckSettingsRepository settings,
        AutomaticCheckCoordinator checks)
    {
        _presence = presence;
        _settings = settings;
        _checks = checks;
        _presence.PauseToggleRequested += OnPauseToggleRequested;
        _checks.Checked += OnChecked;
    }

    /// <summary>Raised when something changed the settings from outside a page.</summary>
    /// <remarks>
    /// The Automatic tasks page listens so that pausing from the icon while the page is open
    /// is visible there immediately, rather than only after the page is opened again.
    /// </remarks>
    public event EventHandler? SettingsChangedOutsideThePage;

    /// <summary>Whether the icon is on screen.</summary>
    public bool IsShowing => _presence.IsShowing;

    /// <summary>
    /// Whether the stored mode says DeskAI should keep checking after its window is closed.
    /// </summary>
    /// <remarks>
    /// Exists so a window's close handler can decide what closing means the instant it happens.
    /// A window-closing event handler cannot await, so the answer has to be a cached value read
    /// synchronously rather than a fresh load from storage. Caching it here,
    /// on the one object every settings change already passes through <see cref="Refresh"/>,
    /// means there is exactly one place to keep it current — a copy kept on a window instead
    /// goes stale the moment the switch changes on another page, because nothing tells the
    /// window a page it isn't showing just changed the mode.
    /// </remarks>
    public bool KeepsRunningWhenClosed { get; private set; }

    /// <summary>
    /// Whether this DeskAI has a notification area to put an icon in at all.
    /// </summary>
    /// <remarks>
    /// False for <see cref="NoBackgroundPresence"/>. The page hides the switch rather than
    /// offering one that cannot work: an inert option still promises something.
    /// </remarks>
    public bool CanShowAnIcon => _presence is not NoBackgroundPresence;

    /// <summary>
    /// Makes the icon match what is stored: shown or not, and saying the right thing.
    /// </summary>
    /// <remarks>
    /// Called after every change rather than only when the mode changes, because the tooltip
    /// is one of the promises. An icon saying DeskAI is looking every 15 minutes while checks
    /// are paused is exactly the failure this feature has to avoid.
    /// </remarks>
    public void Refresh(AutomaticCheckSettings settings)
    {
        KeepsRunningWhenClosed = settings.Mode == AutomaticCheckMode.InBackground;

        if (settings.Mode != AutomaticCheckMode.InBackground)
        {
            _presence.Hide();
            return;
        }

        var tooltip = BackgroundCheckingChoice.Tooltip(settings, _checks.Latest?.ProposalCount);
        if (_presence.IsShowing)
        {
            _presence.Update(tooltip, settings.IsPaused);
        }
        else
        {
            _presence.Show(tooltip, settings.IsPaused);
        }
    }

    /// <summary>
    /// Pause or resume, asked for from the icon rather than from a page.
    /// </summary>
    /// <remarks>
    /// Stopping is always safe, and it is the one control someone may want in a hurry. It
    /// goes through the same stored setting the page's switch uses, so the two cannot hold
    /// different answers, and it cancels a check already under way — someone reaching for a
    /// stop control means the thing happening now.
    /// </remarks>
    private void OnPauseToggleRequested(object? sender, EventArgs args) => _ = TogglePauseAsync();

    /// <summary>
    /// Pause or resume. Awaitable so a test can assert what happened rather than hope.
    /// </summary>
    public async Task TogglePauseAsync()
    {
        try
        {
            var stored = await _settings.LoadAsync().ConfigureAwait(false);
            var updated = stored with { IsPaused = !stored.IsPaused };
            if (updated.IsPaused)
            {
                _checks.StopRunningCheck();
            }

            await _settings.SaveAsync(updated).ConfigureAwait(false);
            Refresh(updated);
            SettingsChangedOutsideThePage?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.Data.Common.DbException
            or IOException
            or OperationCanceledException)
        {
            // There is no page on screen to show this on, and nothing was changed on disk.
            // The icon keeps saying what it said, which is still true.
        }
    }

    /// <summary>Keeps the count on the icon current after a check finishes.</summary>
    private async void OnChecked(object? sender, AutomaticCheckResult result)
    {
        try
        {
            Refresh(await _settings.LoadAsync().ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.Data.Common.DbException
            or IOException
            or OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _presence.PauseToggleRequested -= OnPauseToggleRequested;
        _checks.Checked -= OnChecked;
    }
}
