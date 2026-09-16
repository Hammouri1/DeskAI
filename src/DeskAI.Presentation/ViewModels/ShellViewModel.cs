using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeskAI.App.Services;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;
using DeskAI.Core.Appearance;
using DeskAI.Core.Rules;
using DeskAI.Core.Search;

namespace DeskAI.App.ViewModels;

/// <summary>
/// Backs the permanent scope reminder in the navigation pane.
/// </summary>
/// <remarks>
/// <para>
/// This text was previously a fixed string reading "Sample files only. Your personal
/// folders are not connected." Once a person could connect a real folder, that sentence
/// became false while still being displayed, which is worse than showing nothing: the one
/// label that promises what DeskAI can reach was the label that lied.
/// </para>
/// <para>
/// It is therefore derived from what is actually connected, and refreshed on every
/// navigation, so it cannot drift away from the truth.
/// </para>
/// </remarks>
public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private readonly ConnectedFolderService _folders;
    private readonly AutomaticCheckCoordinator _checks;
    private readonly IAutomaticCheckSettingsRepository _checkSettings;
    private readonly IFindingNotifier _notifier;
    private readonly OrganizeRequest _organize;
    private readonly SearchRequest _search;
    private readonly IAiSettingsRepository _aiSettings;
    private readonly IAppearanceSettingsRepository _appearance;
    private readonly IAppearanceApplier _applier;
    private readonly SynchronizationContext? _uiContext;
    private string _scopeTitle = "Nothing connected yet";
    private string _scopeMessage = "No folders connected. DeskAI cannot see any of your files.";
    private string _findingMessage = string.Empty;
    private string _pageTitle = "Home";
    private string _aiState = "AI off";
    private AppearanceSettings _chosenAppearance = AppearanceSettings.Default;
    private bool _windowIsDark = true;
    private bool _hasFinding;
    private Guid? _folderToReview;
    private bool _disposed;

    public ShellViewModel(
        ConnectedFolderService folders,
        AutomaticCheckCoordinator checks,
        IAutomaticCheckSettingsRepository checkSettings,
        IFindingNotifier notifier,
        OrganizeRequest organize,
        SearchRequest search,
        IAiSettingsRepository aiSettings,
        IAppearanceSettingsRepository appearance,
        IAppearanceApplier applier)
    {
        _folders = folders;
        _checks = checks;
        _checkSettings = checkSettings;
        _notifier = notifier;
        _organize = organize;
        _search = search;
        _aiSettings = aiSettings;
        _appearance = appearance;
        _applier = applier;

        // Captured here because this view model is built on the UI thread, while a check
        // finishes on a background one. Every property set below has to come back. On the
        // WinUI thread this is the dispatcher's context; in a test it is null and the
        // notice is shown directly.
        _uiContext = SynchronizationContext.Current;
        _checks.Checked += OnChecked;
        _checks.Tidied += OnTidied;
        DismissFindingCommand = new RelayCommand(() => HasFinding = false);
    }

    /// <summary>
    /// Tidying while away moved something, or stopped and needs a person. The notice says the
    /// count and the folder's name and offers Review in Organize; the notification, if on,
    /// carries the count only.
    /// </summary>
    private void OnTidied(object? sender, DeskAI.Core.Tidy.AwayTidySummary summary)
    {
        var where = summary.FolderName is { } name ? $" in {name}" : string.Empty;
        var message = summary.MovedAnything
            ? $"While you were away, DeskAI tidied {DeskAI.Core.Tidy.AwayTidyWords.Files(summary.FilesMoved)}{where}. Nothing was deleted."
            : $"DeskAI stopped tidying while you're away{where} and needs you to look.";
        var notification = summary.MovedAnything
            ? $"DeskAI tidied {DeskAI.Core.Tidy.AwayTidyWords.Files(summary.FilesMoved)} while you were away. Open DeskAI to look."
            : "DeskAI stopped tidying while you're away. Open DeskAI to look.";

        void Show()
        {
            FindingMessage = message;
            _folderToReview = summary.FolderToReview;
            OnPropertyChanged(nameof(CanReviewInOrganize));
            HasFinding = true;
            _ = NotifyIfAskedAsync(notification, raw: true);
        }

        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            Show();
            return;
        }

        _uiContext.Post(_ => Show(), null);
    }

    public RelayCommand DismissFindingCommand { get; }

    /// <summary>The menu's routes and the names a person sees for them, in menu order.</summary>
    /// <remarks>
    /// The owner chose on 2026-09-16 to keep these names. The window reads its menu from the
    /// XAML and the top bar reads its title from here, and a test checks the two agree.
    /// </remarks>
    public static IReadOnlyList<(string Route, string Title)> Pages { get; } =
    [
        ("dashboard", "Home"),
        ("organize", "Organize"),
        ("search", "Search"),
        ("automation", "Automatic tasks"),
        ("workspace", "My workspace"),
        ("settings", "Privacy and AI"),
    ];

    /// <summary>The name of the page on screen, shown in the top bar.</summary>
    public string PageTitle
    {
        get => _pageTitle;
        private set => SetProperty(ref _pageTitle, value);
    }

    /// <summary>Tells the top bar which page is on screen. An unknown route leaves the title alone.</summary>
    public void ShowPage(string route)
    {
        foreach (var (candidate, title) in Pages)
        {
            if (string.Equals(candidate, route, StringComparison.Ordinal))
            {
                PageTitle = title;
                return;
            }
        }
    }

    /// <summary>
    /// The AI pill in the top bar: "AI off", "AI on this computer", or "AI: OpenRouter".
    /// </summary>
    /// <remarks>
    /// Read from the saved AI choice on every refresh, so the pill can never claim AI is off
    /// while a service is set up, or name a service after its key was removed. Pressing the
    /// pill only opens Privacy and AI.
    /// </remarks>
    public string AiState
    {
        get => _aiState;
        private set => SetProperty(ref _aiState, value);
    }

    /// <summary>Words the AI pill. Only a service that is actually ready to be asked is named.</summary>
    public static string DescribeAi(AiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Mode == AiMode.Local
            && Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var local)
            && local.IsLoopback)
        {
            return "AI on this computer";
        }

        if (settings.Mode == AiMode.Cloud
            && settings.CloudConsentGranted
            && settings.CredentialReference is not null
            && CloudProviderCatalog.Find(settings.ProviderId) is { } provider)
        {
            return $"AI: {provider.DisplayName}";
        }

        return "AI off";
    }

    /// <summary>
    /// The dark-mode switch in the pane: on when DeskAI's window is dark right now.
    /// </summary>
    /// <remarks>
    /// While the saved choice is "Follow Windows" the switch shows what Windows chose, which the
    /// window reports through <see cref="ReportWindowTheme"/>; this project cannot see WinUI.
    /// Flipping the switch saves an explicit Light or Dark, the same setting My workspace edits.
    /// It changes DeskAI's window only.
    /// </remarks>
    public bool IsDark => _chosenAppearance.Mode switch
    {
        ThemeMode.Dark => true,
        ThemeMode.Light => false,
        _ => _windowIsDark,
    };

    /// <summary>The window says which theme it is actually showing while following Windows.</summary>
    public void ReportWindowTheme(bool isDark)
    {
        _windowIsDark = isDark;
        OnPropertyChanged(nameof(IsDark));
    }

    /// <summary>Saves always-dark or always-light and repaints DeskAI's window. Nothing in Windows changes.</summary>
    public async Task SetDarkAsync(bool isDark)
    {
        var mode = isDark ? ThemeMode.Dark : ThemeMode.Light;
        if (_chosenAppearance.Mode == mode)
        {
            return;
        }

        var settings = _chosenAppearance with { Mode = mode };
        try
        {
            await _appearance.SaveAsync(settings).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.Data.Common.DbException
            or IOException)
        {
            // The switch snaps back to what is saved; a repaint that would not survive a
            // restart is more confusing than one that did not happen.
            OnPropertyChanged(nameof(IsDark));
            return;
        }

        _chosenAppearance = settings;
        _applier.Apply(settings);
        OnPropertyChanged(nameof(IsDark));
    }

    /// <summary>
    /// The search box in the top bar: leaves the phrase for Search to run when it opens.
    /// </summary>
    /// <returns>True when there is a phrase to search for; the window then opens Search.</returns>
    /// <remarks>
    /// It goes through the same one-shot request "Open in Search" uses, so arriving from the top
    /// bar shows nothing a person could not have typed on Search. A blank phrase does nothing.
    /// </remarks>
    public bool FindFile(string? phrase)
    {
        var trimmed = phrase?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return false;
        }

        _search.AskPhrase(trimmed);
        return true;
    }

    /// <summary>
    /// The quiet notice in the top corner: something matched, and it is waiting.
    /// </summary>
    /// <remarks>
    /// It stays until it is looked at or dismissed rather than fading. A check can finish
    /// while nobody is at the machine, and a message that disappeared before anyone saw it
    /// is the same as never having shown it.
    /// </remarks>
    public bool HasFinding
    {
        get => _hasFinding;
        private set => SetProperty(ref _hasFinding, value);
    }

    public string FindingMessage
    {
        get => _findingMessage;
        private set => SetProperty(ref _findingMessage, value);
    }

    /// <summary>Whether the notice knows which folder its matches are in.</summary>
    public bool CanReviewInOrganize => _folderToReview is not null;

    /// <summary>
    /// "Review in Organize": leaves the folder with the most matches for the Organize page to
    /// open, and puts the notice away. The window then opens the page. Nothing else happens —
    /// the files move only if the person presses Tidy there.
    /// </summary>
    public void ReviewInOrganize()
    {
        if (_folderToReview is { } folderId)
        {
            _organize.Ask(folderId);
        }

        HasFinding = false;
    }

    /// <summary>
    /// Puts what a finished check found in front of someone, in the app and — only if they
    /// asked for it — in a Windows notification.
    /// </summary>
    /// <remarks>
    /// A check that found nothing says nothing. Announcing "nothing matched" every fifteen
    /// minutes would train a person to ignore the one time it says something did.
    /// </remarks>
    private void OnChecked(object? sender, AutomaticCheckResult result)
    {
        if (!result.HasSomethingToReview)
        {
            return;
        }

        var message = result.ProposalCount == 1
            ? "1 file matches your rules."
            : $"{result.ProposalCount} files match your rules.";

        void Show()
        {
            FindingMessage = $"{message} Nothing has moved.";
            _folderToReview = result.FolderToReview;
            OnPropertyChanged(nameof(CanReviewInOrganize));
            HasFinding = true;
            _ = NotifyIfAskedAsync(message);
        }

        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            Show();
            return;
        }

        _uiContext.Post(_ => Show(), null);
    }

    private async Task NotifyIfAskedAsync(string message, bool raw = false)
    {
        try
        {
            var settings = await _checkSettings.LoadAsync().ConfigureAwait(true);
            if (settings.NotifyWhenSomethingIsFound)
            {
                _notifier.Notify("DeskAI", raw ? message : $"{message} Open DeskAI to look.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.Data.Common.DbException
            or IOException)
        {
            // The in-app notice has already been shown. A notification that could not be
            // sent is not worth interrupting anyone about.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _checks.Checked -= OnChecked;
        _checks.Tidied -= OnTidied;
    }

    public string ScopeTitle
    {
        get => _scopeTitle;
        private set => SetProperty(ref _scopeTitle, value);
    }

    public string ScopeMessage
    {
        get => _scopeMessage;
        private set => SetProperty(ref _scopeMessage, value);
    }

    /// <summary>Re-reads what is connected, the AI choice, and the look. Cheap, and called on every navigation.</summary>
    public async Task RefreshAsync()
    {
        await RefreshAiAndLookAsync().ConfigureAwait(true);

        IReadOnlyList<ConnectedFolder> connected;
        try
        {
            connected = await _folders.ListAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.Data.Common.DbException
            or IOException)
        {
            // Never fall back to the reassuring message. If the real scope cannot be read,
            // say that rather than implying nothing is connected.
            ScopeTitle = "Scope unavailable";
            ScopeMessage = "DeskAI could not check which folders are connected.";
            return;
        }

        if (connected.Count == 0)
        {
            ScopeTitle = "Nothing connected yet";
            ScopeMessage = "No folders connected. DeskAI cannot see any of your files. Connect your Desktop, Downloads, Documents, or Pictures on Home.";
            return;
        }

        var files = connected.Sum(folder => folder.FileCount);
        ScopeTitle = connected.Count == 1 ? "1 folder connected" : $"{connected.Count} folders connected";

        // "It has not opened any of them" stops being true the moment someone allows reading
        // inside a folder, so the reminder counts those folders instead of repeating a
        // reassurance that has quietly expired.
        var reading = connected.Count(folder => folder.CanReadContent);
        ScopeMessage = reading == 0
            ? $"DeskAI remembers names, sizes, and dates for {files} file(s). It has not opened any of them."
            : $"DeskAI remembers names, sizes, and dates for {files} file(s). You have let it read inside "
                + (reading == 1 ? "1 folder." : $"{reading} folders.");

        // Moving files is the permission that matters most, so it is counted here too rather
        // than being visible only on the page where it was given.
        var tidying = connected.Count(folder => folder.CanTidy);
        if (tidying > 0)
        {
            ScopeMessage += tidying == 1
                ? " You have let it tidy 1 folder when you press Tidy."
                : $" You have let it tidy {tidying} folders when you press Tidy.";
        }
    }

    private async Task RefreshAiAndLookAsync()
    {
        try
        {
            AiState = DescribeAi(await _aiSettings.LoadAsync().ConfigureAwait(true));
            _chosenAppearance = await _appearance.LoadAsync().ConfigureAwait(true);
            OnPropertyChanged(nameof(IsDark));
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.Data.Common.DbException
            or IOException)
        {
            // The pill must not guess. "AI off" is the only state that can be wrong in the
            // safe direction: it never names a service that might not be set up.
            AiState = "AI off";
        }
    }
}
