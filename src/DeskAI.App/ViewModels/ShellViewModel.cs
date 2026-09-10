using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeskAI.App.Services;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Rules;
using DeskAI.Core.Search;
using Microsoft.UI.Dispatching;

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
    private readonly DispatcherQueue? _dispatcher;
    private string _scopeTitle = "Practice mode";
    private string _scopeMessage = "No folders connected. DeskAI cannot see any of your files.";
    private string _findingMessage = string.Empty;
    private bool _hasFinding;
    private bool _disposed;

    public ShellViewModel(
        ConnectedFolderService folders,
        AutomaticCheckCoordinator checks,
        IAutomaticCheckSettingsRepository checkSettings,
        IFindingNotifier notifier)
    {
        _folders = folders;
        _checks = checks;
        _checkSettings = checkSettings;
        _notifier = notifier;

        // Captured here because this view model is built on the UI thread, while a check
        // finishes on a background one. Every property set below has to come back.
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _checks.Checked += OnChecked;
        DismissFindingCommand = new RelayCommand(() => HasFinding = false);
    }

    public RelayCommand DismissFindingCommand { get; }

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
            HasFinding = true;
            _ = NotifyIfAskedAsync(message);
        }

        if (_dispatcher is null || _dispatcher.HasThreadAccess)
        {
            Show();
            return;
        }

        _dispatcher.TryEnqueue(Show);
    }

    private async Task NotifyIfAskedAsync(string message)
    {
        try
        {
            var settings = await _checkSettings.LoadAsync().ConfigureAwait(true);
            if (settings.NotifyWhenSomethingIsFound)
            {
                _notifier.Notify("DeskAI", $"{message} Open DeskAI to look.");
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

    /// <summary>Re-reads what is connected. Cheap, and called on every navigation.</summary>
    public async Task RefreshAsync()
    {
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
            ScopeTitle = "Practice mode";
            ScopeMessage = "No folders connected. DeskAI cannot see any of your files.";
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
    }
}
