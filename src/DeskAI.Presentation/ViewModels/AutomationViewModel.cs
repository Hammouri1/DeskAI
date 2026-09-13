using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeskAI.App.Services;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Rules;

namespace DeskAI.App.ViewModels;

/// <summary>One stored rule, written out the way a person can check it.</summary>
public sealed record RuleViewModel(Guid Id, string Name, string Sentence, bool IsEnabled)
{
    public string State => IsEnabled ? "On" : "Off";

    public string ToggleAction => IsEnabled ? "Turn off" : "Turn on";
}

/// <summary>One file a practice run would move, and where it would go.</summary>
public sealed record RuleProposalViewModel(string Folder, string File, string Destination, string Reason);

/// <summary>Files the rules disagreed about, which are therefore left alone.</summary>
public sealed record RuleConflictViewModel(string Folder, string File, string Explanation);

/// <summary>One past check, written out for the history list.</summary>
public sealed record CheckRunViewModel(string When, string What, string? Note)
{
    public bool HasNote => !string.IsNullOrEmpty(Note);
}

/// <summary>One choice of how often DeskAI looks, in the words it is offered by.</summary>
public sealed record CheckFrequencyOption(AutomaticCheckFrequency Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Drives the Automatic tasks page: write rules, and see what they would do.
/// </summary>
/// <remarks>
/// <para>
/// Everything here stops at a practice run. There is deliberately no button that carries a
/// rule out: rules produce proposals, and a proposal still has to become a plan that is
/// checked, previewed, and approved. Adding a "run it" button here would be a way around
/// the preview, not a feature.
/// </para>
/// <para>
/// Automatic checks may look on a schedule while DeskAI is open, but looking is all they do.
/// The page says DeskAI never moves a file on its own, because a rules screen is exactly
/// where someone would reasonably assume otherwise.
/// </para>
/// </remarks>
public sealed class AutomationViewModel : ObservableObject, IDisposable
{
    private readonly IRuleRepository _rules;
    private readonly RuleSimulationService _simulation;
    private readonly IAutomaticCheckSettingsRepository _checkSettings;
    private readonly IAutomaticCheckHistoryRepository _checkHistory;
    private readonly AutomaticCheckCoordinator _checks;
    private readonly IClock _clock;
    private readonly BackgroundPresenceController _presence;
    private CheckFrequencyOption _selectedFrequency;
    private bool _isPaused;
    private bool _notifyWhenSomethingIsFound;
    private bool _isApplyingStoredSettings;
    private AutomaticCheckMode _mode = AutomaticCheckMode.WhileAppIsOpen;
    private string _lastCheckedDescription = "DeskAI has not checked yet.";
    private string _sentence = string.Empty;
    private string _newRuleName = string.Empty;
    private string _newRuleNameContains = string.Empty;
    private string _newRuleExtension = string.Empty;
    private string _newRuleDestination = string.Empty;
    private string _message = string.Empty;
    private string _formMessage = string.Empty;
    private string _practiceHeadline = string.Empty;
    private string _practiceDetail = string.Empty;
    private bool _hasPractised;
    private bool _isBusy;
    private bool _disposed;

    public AutomationViewModel(
        IRuleRepository rules,
        RuleSimulationService simulation,
        IAutomaticCheckSettingsRepository checkSettings,
        IAutomaticCheckHistoryRepository checkHistory,
        AutomaticCheckCoordinator checks,
        IClock clock,
        BackgroundPresenceController presence)
    {
        _rules = rules;
        _simulation = simulation;
        _checkSettings = checkSettings;
        _checkHistory = checkHistory;
        _checks = checks;
        _clock = clock;
        _presence = presence;
        _selectedFrequency = FrequencyOptions[1];
        CheckNowCommand = new AsyncRelayCommand(CheckNowAsync, () => !IsBusy);
        ClearHistoryCommand = new AsyncRelayCommand(ClearHistoryAsync, () => !IsBusy);
        AddRuleCommand = new AsyncRelayCommand(AddRuleAsync, () => !IsBusy);
        DraftFromSentenceCommand = new RelayCommand(DraftFromSentence, () => !IsBusy);
        PractiseCommand = new AsyncRelayCommand(PractiseAsync, () => !IsBusy);
        ToggleRuleCommand = new AsyncRelayCommand<Guid>(ToggleRuleAsync, _ => !IsBusy);
        DeleteRuleCommand = new AsyncRelayCommand<Guid>(DeleteRuleAsync, _ => !IsBusy);

        // The controller is a singleton but this view model is transient, so subscribing to
        // it here — once per instance, unsubscribed in Dispose — rather than to
        // IBackgroundPresence's own events keeps one menu click from toggling pause once per
        // page visit that ever happened.
        _presence.SettingsChangedOutsideThePage += OnSettingsChangedOutsideThePage;
    }

    public ObservableCollection<RuleViewModel> Rules { get; } = [];

    public ObservableCollection<RuleProposalViewModel> Proposals { get; } = [];

    public ObservableCollection<RuleConflictViewModel> Conflicts { get; } = [];

    public AsyncRelayCommand AddRuleCommand { get; }

    public RelayCommand DraftFromSentenceCommand { get; }

    public AsyncRelayCommand PractiseCommand { get; }

    public AsyncRelayCommand<Guid> ToggleRuleCommand { get; }

    public AsyncRelayCommand<Guid> DeleteRuleCommand { get; }

    public AsyncRelayCommand CheckNowCommand { get; }

    public AsyncRelayCommand ClearHistoryCommand { get; }

    /// <summary>
    /// The checks that have happened, most recent first.
    /// </summary>
    /// <remarks>
    /// Shown because automatic behaviour a person cannot look back at is automatic behaviour
    /// they have to take on trust. Interrupted and failed checks appear here too: a history
    /// that quietly omitted them would be a reassuring one rather than an accurate one.
    /// </remarks>
    public ObservableCollection<CheckRunViewModel> RecentChecks { get; } = [];

    public bool HasRecentChecks => RecentChecks.Count > 0;

    public bool HasNoRecentChecks => RecentChecks.Count == 0;

    /// <summary>
    /// How often DeskAI may look, offered in words rather than in minutes.
    /// </summary>
    /// <remarks>
    /// "Only when I ask" is first because it is the choice that promises the least, and a
    /// list of automatic options with no way out reads as though there were none.
    /// </remarks>
    public IReadOnlyList<CheckFrequencyOption> FrequencyOptions { get; } =
    [
        new(AutomaticCheckFrequency.OnlyWhenIAsk, "Only when I ask"),
        new(AutomaticCheckFrequency.EveryFifteenMinutes, "Every 15 minutes"),
        new(AutomaticCheckFrequency.EveryHour, "Every hour"),
        new(AutomaticCheckFrequency.ACoupleOfTimesADay, "A few times a day"),
    ];

    public CheckFrequencyOption SelectedFrequency
    {
        get => _selectedFrequency;
        set
        {
            if (SetProperty(ref _selectedFrequency, value))
            {
                OnPropertyChanged(nameof(AutomaticCheckSummary));
                SaveCheckSettings();
                RefreshPresence();
            }
        }
    }

    /// <summary>The kill switch. Stops the next check and the one happening now.</summary>
    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (SetProperty(ref _isPaused, value))
            {
                if (value)
                {
                    // Someone reaching for a stop control means the activity happening now,
                    // not merely the next one.
                    _checks.StopRunningCheck();
                }

                OnPropertyChanged(nameof(AutomaticCheckSummary));
                SaveCheckSettings();
                RefreshPresence();
            }
        }
    }

    /// <summary>
    /// Whether Windows should show a notification when a check finds something.
    /// </summary>
    /// <remarks>
    /// Off unless someone turns it on. A notification arrives without being asked for, so it
    /// is not something a person should have to discover and switch off.
    /// </remarks>
    public bool NotifyWhenSomethingIsFound
    {
        get => _notifyWhenSomethingIsFound;
        set
        {
            if (SetProperty(ref _notifyWhenSomethingIsFound, value))
            {
                SaveCheckSettings();
            }
        }
    }

    public string LastCheckedDescription
    {
        get => _lastCheckedDescription;
        private set => SetProperty(ref _lastCheckedDescription, value);
    }

    /// <summary>
    /// Whether this DeskAI can offer to keep running with no window at all.
    /// </summary>
    /// <remarks>
    /// False when there is no notification area to show an icon in. The switch is then
    /// absent rather than present and inert: an option that cannot work is worse than no
    /// option, because it promises something.
    /// </remarks>
    public bool CanKeepRunning => _presence.CanShowAnIcon;

    /// <summary>Whether DeskAI keeps checking after the window is closed.</summary>
    public bool KeepsRunningWhenClosed => _mode == AutomaticCheckMode.InBackground;

    /// <summary>The "More details" paragraph, which changes with the mode.</summary>
    public string MoreDetails => BackgroundCheckingChoice.MoreDetails(_mode);

    /// <summary>
    /// The words someone is shown before this is turned on. Stores nothing.
    /// </summary>
    /// <remarks>
    /// Asking is separated from doing so that a page test can assert what a person was
    /// actually told, and so that closing the dialog is genuinely a decision not to.
    /// </remarks>
    public BackgroundCheckingQuestion AskAboutKeepingRunning() =>
        BackgroundCheckingChoice.Ask(CurrentSettings());

    /// <summary>The person said yes, together with what they chose about notifications.</summary>
    public async Task KeepRunningAsync(bool notifyWhenSomethingIsFound)
    {
        _mode = AutomaticCheckMode.InBackground;
        _notifyWhenSomethingIsFound = notifyWhenSomethingIsFound;
        OnPropertyChanged(nameof(NotifyWhenSomethingIsFound));
        OnPropertyChanged(nameof(KeepsRunningWhenClosed));
        OnPropertyChanged(nameof(MoreDetails));
        OnPropertyChanged(nameof(AutomaticCheckSummary));
        await SaveCheckSettingsAsync().ConfigureAwait(true);
        RefreshPresence();
    }

    /// <summary>The person turned it off. The icon goes at once.</summary>
    public async Task StopKeepingRunningAsync()
    {
        _mode = AutomaticCheckMode.WhileAppIsOpen;
        OnPropertyChanged(nameof(KeepsRunningWhenClosed));
        OnPropertyChanged(nameof(MoreDetails));
        OnPropertyChanged(nameof(AutomaticCheckSummary));
        await SaveCheckSettingsAsync().ConfigureAwait(true);
        RefreshPresence();
    }

    private AutomaticCheckSettings CurrentSettings() => new(
        _mode,
        SelectedFrequency.Value,
        IsPaused,
        NotifyWhenSomethingIsFound);

    /// <summary>Hands the current settings to the one thing that owns the icon.</summary>
    private void RefreshPresence() => _presence.Refresh(CurrentSettings());

    /// <summary>
    /// Something outside this page changed the settings — pause, from the icon's menu.
    /// </summary>
    /// <remarks>
    /// Re-reads the whole stored record, not only the pause flag: a page opened before
    /// someone turned background checking on elsewhere would otherwise keep believing the
    /// old mode, and the icon's own <c>RefreshPresence</c> call — made from this instance's
    /// <see cref="IsPaused"/> setter with this instance's stale <c>_mode</c> — would then hide
    /// the icon on the very next pause or resume, even though the store still says it should
    /// be showing. The flag keeps this from counting as a fresh decision and writing the
    /// value straight back.
    /// </remarks>
    private async void OnSettingsChangedOutsideThePage(object? sender, EventArgs args)
    {
        try
        {
            var stored = await _checkSettings.LoadAsync().ConfigureAwait(true);
            _isApplyingStoredSettings = true;
            try
            {
                _mode = stored.Mode;
                IsPaused = stored.IsPaused;
            }
            finally
            {
                _isApplyingStoredSettings = false;
            }

            OnPropertyChanged(nameof(KeepsRunningWhenClosed));
            OnPropertyChanged(nameof(MoreDetails));
            OnPropertyChanged(nameof(AutomaticCheckSummary));
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI could not read that setting: {exception.Message}";
        }
    }

    /// <summary>
    /// What DeskAI is actually doing, in one sentence, derived from the current choice.
    /// </summary>
    /// <remarks>
    /// Written rather than assembled from the label so each case reads naturally, and so
    /// every one of them ends by saying nothing is moved. That sentence is the point: a
    /// screen that says DeskAI is watching your folders has to say in the same breath what
    /// watching is allowed to lead to.
    /// </remarks>
    public string AutomaticCheckSummary => IsPaused
        ? "Automatic checks are paused. DeskAI is not looking at anything on its own."
        : (SelectedFrequency.Value, KeepsRunningWhenClosed) switch
        {
            (AutomaticCheckFrequency.OnlyWhenIAsk, _) =>
                "DeskAI only looks when you press Check now. It never moves anything by itself.",
            (AutomaticCheckFrequency.EveryFifteenMinutes, true) =>
                "DeskAI keeps looking every 15 minutes, even after you close the window, and "
                    + "tells you if your rules match anything. It never moves anything by itself.",
            (AutomaticCheckFrequency.EveryHour, true) =>
                "DeskAI keeps looking every hour, even after you close the window, and tells "
                    + "you if your rules match anything. It never moves anything by itself.",
            (_, true) =>
                "DeskAI keeps looking a few times a day, even after you close the window, and "
                    + "tells you if your rules match anything. It never moves anything by itself.",
            (AutomaticCheckFrequency.EveryFifteenMinutes, false) =>
                "While DeskAI is open it looks every 15 minutes and tells you if your rules "
                    + "match anything. It never moves anything by itself.",
            (AutomaticCheckFrequency.EveryHour, false) =>
                "While DeskAI is open it looks every hour and tells you if your rules match "
                    + "anything. It never moves anything by itself.",
            _ => "While DeskAI is open it looks a few times a day and tells you if your rules "
                + "match anything. It never moves anything by itself.",
        };

    /// <summary>A sentence someone typed, waiting to be read into the form.</summary>
    public string Sentence
    {
        get => _sentence;
        set => SetProperty(ref _sentence, value);
    }

    public string NewRuleName
    {
        get => _newRuleName;
        set
        {
            if (SetProperty(ref _newRuleName, value))
            {
                ClearFormMessage();
            }
        }
    }

    public string NewRuleNameContains
    {
        get => _newRuleNameContains;
        set
        {
            if (SetProperty(ref _newRuleNameContains, value))
            {
                ClearFormMessage();
            }
        }
    }

    public string NewRuleExtension
    {
        get => _newRuleExtension;
        set
        {
            if (SetProperty(ref _newRuleExtension, value))
            {
                ClearFormMessage();
            }
        }
    }

    public string NewRuleDestination
    {
        get => _newRuleDestination;
        set
        {
            if (SetProperty(ref _newRuleDestination, value))
            {
                ClearFormMessage();
            }
        }
    }

    /// <summary>
    /// What happened the last time Save was pressed, shown beside the button.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="Message"/>, which describes the rule list further up the
    /// page. A refusal shown next to the list is a refusal nobody reads: the person is at
    /// the bottom of the page looking at the form, and the answer to what they just did has
    /// to be where they are looking.
    /// </remarks>
    public string FormMessage
    {
        get => _formMessage;
        private set
        {
            if (SetProperty(ref _formMessage, value))
            {
                OnPropertyChanged(nameof(HasFormMessage));
            }
        }
    }

    public bool HasFormMessage => !string.IsNullOrEmpty(FormMessage);

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public string PracticeHeadline
    {
        get => _practiceHeadline;
        private set => SetProperty(ref _practiceHeadline, value);
    }

    public string PracticeDetail
    {
        get => _practiceDetail;
        private set => SetProperty(ref _practiceDetail, value);
    }

    public bool HasPractised
    {
        get => _hasPractised;
        private set => SetProperty(ref _hasPractised, value);
    }

    public bool HasRules => Rules.Count > 0;

    public bool HasProposals => Proposals.Count > 0;

    public bool HasConflicts => Conflicts.Count > 0;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                AddRuleCommand.NotifyCanExecuteChanged();
                CheckNowCommand.NotifyCanExecuteChanged();
                ClearHistoryCommand.NotifyCanExecuteChanged();
                PractiseCommand.NotifyCanExecuteChanged();
                ToggleRuleCommand.NotifyCanExecuteChanged();
                DeleteRuleCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(IsIdle));
            }
        }
    }

    public bool IsIdle => !IsBusy;

    public async Task InitializeAsync()
    {
        try
        {
            await ReloadAsync().ConfigureAwait(true);
            await LoadCheckSettingsAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI could not read your rules: {exception.Message}";
        }
    }

    /// <summary>
    /// Reads the stored choice into the boxes without treating that as someone changing it.
    /// </summary>
    /// <remarks>
    /// The flag matters: assigning to the properties raises their setters, and without it
    /// simply opening the page would write the settings back and count as a fresh decision.
    /// </remarks>
    private async Task LoadCheckSettingsAsync()
    {
        var stored = await _checkSettings.LoadAsync().ConfigureAwait(true);
        var lastChecked = await _checkSettings.ReadLastCheckedAtUtcAsync().ConfigureAwait(true);

        _isApplyingStoredSettings = true;
        try
        {
            _mode = stored.Mode;
            SelectedFrequency = FrequencyOptions.FirstOrDefault(option => option.Value == stored.Frequency)
                ?? FrequencyOptions[1];
            IsPaused = stored.IsPaused;
            NotifyWhenSomethingIsFound = stored.NotifyWhenSomethingIsFound;
        }
        finally
        {
            _isApplyingStoredSettings = false;
        }

        RefreshPresence();
        OnPropertyChanged(nameof(KeepsRunningWhenClosed));
        OnPropertyChanged(nameof(MoreDetails));

        DescribeLastCheck(lastChecked);
        await ReloadHistoryAsync().ConfigureAwait(true);
    }

    /// <summary>How many past checks the page shows. The store keeps no more than 50.</summary>
    private const int RecentChecksShown = 10;

    private async Task ReloadHistoryAsync()
    {
        var runs = await _checkHistory.ListRecentAsync(RecentChecksShown).ConfigureAwait(true);
        RecentChecks.Clear();
        foreach (var run in runs)
        {
            var when = run.StartedAtUtc.ToLocalTime();
            RecentChecks.Add(new CheckRunViewModel(
                $"{when:t} on {when:d}",
                run.Describe(),
                run.CatchUpNote));
        }

        OnPropertyChanged(nameof(HasRecentChecks));
        OnPropertyChanged(nameof(HasNoRecentChecks));
    }

    private async Task ClearHistoryAsync()
    {
        IsBusy = true;
        try
        {
            await _checkHistory.ClearAsync().ConfigureAwait(true);
            await ReloadHistoryAsync().ConfigureAwait(true);
            Message = "Check history cleared.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void DescribeLastCheck(DateTimeOffset? lastCheckedUtc)
    {
        if (lastCheckedUtc is not { } checkedAt)
        {
            LastCheckedDescription = "DeskAI has not looked yet.";
            return;
        }

        var elapsed = _clock.UtcNow - checkedAt;
        LastCheckedDescription = elapsed < TimeSpan.FromMinutes(2)
            ? "Last looked: just now."
            : $"Last looked: {checkedAt.ToLocalTime():t} on {checkedAt.ToLocalTime():d}.";
    }

    /// <summary>
    /// Stores the choice as soon as it is made, so nothing has to be confirmed.
    /// </summary>
    /// <remarks>
    /// A failure to save is shown rather than swallowed. Silently keeping an on-screen
    /// setting that did not persist would leave someone believing checks were paused when
    /// the next launch would resume them.
    /// </remarks>
    private async void SaveCheckSettings()
    {
        if (_isApplyingStoredSettings)
        {
            return;
        }

        await SaveCheckSettingsAsync().ConfigureAwait(true);
    }

    private async Task SaveCheckSettingsAsync()
    {
        try
        {
            await _checkSettings.SaveAsync(CurrentSettings()).ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI could not save that setting: {exception.Message}";
        }
    }

    /// <summary>
    /// Looks now because someone asked. Still only looks.
    /// </summary>
    /// <remarks>
    /// This refreshes what DeskAI remembers about each connected folder and runs the same
    /// practice run the button below does. It ends at a count on this page; there is no
    /// path from here to a file moving.
    /// </remarks>
    private async Task CheckNowAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _checks.RunNowAsync().ConfigureAwait(true);
            if (result is null)
            {
                Message = "DeskAI is already looking. One moment.";
                return;
            }

            DescribeLastCheck(result.CheckedAtUtc);
            await ReloadHistoryAsync().ConfigureAwait(true);
            Message = result.HasSomethingToReview
                ? $"Your rules match {result.ProposalCount} file(s). Try a practice run to see them. "
                    + "Nothing has moved."
                : $"Nothing in your {result.FoldersChecked} connected folder(s) matches your rules right now.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddRuleAsync()
    {
        IsBusy = true;
        try
        {
            var conditions = new List<RuleCondition>();
            if (!string.IsNullOrWhiteSpace(NewRuleNameContains))
            {
                conditions.Add(new NameContainsCondition(NewRuleNameContains.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(NewRuleExtension))
            {
                conditions.Add(new ExtensionIsCondition(NewRuleExtension.Trim()));
            }

            // The domain refuses a rule with no conditions because it would match every
            // file. Saying so here is friendlier than letting the exception be the message.
            if (conditions.Count == 0)
            {
                FormMessage = "Add at least one thing to look for, or this rule would match every file.";
                return;
            }

            var rule = AutomationRule.Create(
                Guid.NewGuid(),
                NewRuleName.Trim(),
                conditions,
                new MoveToFolderAction(NewRuleDestination.Trim()));

            await _rules.SaveAsync(rule).ConfigureAwait(true);
            NewRuleName = string.Empty;
            NewRuleNameContains = string.Empty;
            NewRuleExtension = string.Empty;
            NewRuleDestination = string.Empty;
            await ReloadAsync().ConfigureAwait(true);

            // Set after clearing the boxes, because clearing them wipes the form message.
            FormMessage = $"Saved: {rule.Describe()} It is in the list above. Nothing has moved.";
            Message = "Try a practice run to see what your rules would do.";
        }
        catch (ArgumentException exception)
        {
            // Every refusal in the rule domain is written for a person to read, so the
            // message can be shown as-is rather than replaced with something vaguer.
            FormMessage = exception.Message;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            FormMessage = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ToggleRuleAsync(Guid ruleId)
    {
        IsBusy = true;
        try
        {
            var rule = await _rules.FindAsync(ruleId).ConfigureAwait(true);
            if (rule is null)
            {
                Message = "That rule is no longer there.";
                return;
            }

            await _rules.SaveAsync(rule.WithEnabled(!rule.IsEnabled)).ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
            Message = rule.IsEnabled
                ? $"\"{rule.Name}\" is off. It will not be included in a practice run."
                : $"\"{rule.Name}\" is on.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteRuleAsync(Guid ruleId)
    {
        IsBusy = true;
        try
        {
            await _rules.RemoveAsync(ruleId).ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
            Message = "Rule deleted. No files were touched.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Shows what the rules would do. It does none of it.
    /// </summary>
    private async Task PractiseAsync()
    {
        IsBusy = true;
        try
        {
            var simulation = await _simulation.SimulateAsync(_clock.UtcNow).ConfigureAwait(true);
            Apply(simulation);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            HasPractised = false;
            Message = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(RuleSimulation simulation)
    {
        Proposals.Clear();
        Conflicts.Clear();

        foreach (var folder in simulation.Folders)
        {
            foreach (var proposal in folder.Preview.Proposals)
            {
                Proposals.Add(new RuleProposalViewModel(
                    folder.RootName,
                    proposal.RelativePath,
                    proposal.DestinationRelativeDirectory,
                    proposal.Reason));
            }

            foreach (var conflict in folder.Preview.Conflicts)
            {
                Conflicts.Add(new RuleConflictViewModel(
                    folder.RootName,
                    conflict.RelativePath,
                    conflict.Explanation));
            }
        }

        HasPractised = true;
        PracticeHeadline = simulation.ProposalCount switch
        {
            0 when simulation.RulesConsidered == 0 => "No rules are on",
            0 when simulation.Folders.Count == 0 => "No folders are connected",
            0 => "Nothing would move",
            1 => "1 file would move",
            _ => $"{simulation.ProposalCount} files would move",
        };

        PracticeDetail = simulation.RulesConsidered == 0
            ? "Turn a rule on, then try again."
            : simulation.Folders.Count == 0
                ? "Connect a folder in Search first, so there is something for rules to look at."
                : "This is a practice run. Nothing has moved, and DeskAI cannot move anything "
                    + "from this page — any real change still goes through the preview where you approve it.";

        OnPropertyChanged(nameof(HasProposals));
        OnPropertyChanged(nameof(HasConflicts));
    }

    private async Task ReloadAsync()
    {
        var stored = await _rules.ListAsync().ConfigureAwait(true);
        Rules.Clear();
        foreach (var rule in stored)
        {
            Rules.Add(new RuleViewModel(rule.Id, rule.Name, rule.Describe(), rule.IsEnabled));
        }

        OnPropertyChanged(nameof(HasRules));

        // Always restated from the list itself. Setting it only when the list was empty left
        // "No rules yet" on screen above rules that had just loaded. Callers that did
        // something specific overwrite this with their own sentence afterwards.
        Message = Rules.Count == 0
            ? "No rules yet. Write one below and try a practice run."
            : "Try a practice run to see what your rules would do.";
    }

    /// <summary>
    /// Reads a typed sentence into the form, for the person to check and change.
    /// </summary>
    /// <remarks>
    /// It fills the boxes and stops. Nothing is saved, because DeskAI understanding a
    /// sentence is not the same as someone agreeing to what it understood — the whole point
    /// of drafting is that the reading is visible before it becomes a rule.
    /// </remarks>
    private void DraftFromSentence()
    {
        RuleDraft draft;
        try
        {
            draft = RuleDraftTranslator.Draft(Sentence);
        }
        catch (ArgumentException exception)
        {
            FormMessage = exception.Message;
            return;
        }

        if (!draft.UnderstoodAnything)
        {
            FormMessage = "DeskAI did not understand any of that. Try something like "
                + "\"move invoices to Documents\", or fill the boxes in yourself.";
            return;
        }

        // A new sentence replaces the whole draft. Filling only what was understood would
        // leave parts of the last one behind, so a sentence that never mentioned PDFs would
        // quietly keep ".pdf" from the sentence before it and save a rule nobody described.
        // The rule's name is left alone, because drafting never sets one.
        NewRuleNameContains = string.Empty;
        NewRuleExtension = string.Empty;
        NewRuleDestination = string.Empty;

        foreach (var condition in draft.Conditions)
        {
            switch (condition)
            {
                case NameContainsCondition text:
                    NewRuleNameContains = text.Text;
                    break;
                case ExtensionIsCondition ending:
                    NewRuleExtension = ending.Extension;
                    break;
                default:
                    // Sizes and ages have no box on this form yet. Saying so is better than
                    // dropping part of what was understood without a word.
                    break;
            }
        }

        if (draft.Action is not null)
        {
            NewRuleDestination = draft.Action.DestinationRelativeDirectory;
        }

        // Set last: filling the boxes above clears the form message.
        FormMessage = Describe(draft);
    }

    private static string Describe(RuleDraft draft)
    {
        var understood = string.Join(", ", draft.Chips.Select(chip => chip.Label));
        var problem = draft.DestinationProblem is null ? string.Empty : $" {draft.DestinationProblem}";
        var unsupported = draft.Conditions.Any(condition =>
            condition is LargerThanCondition or SmallerThanCondition or OlderThanCondition)
            ? " Sizes and dates cannot be typed into this form yet, so that part was left out."
            : string.Empty;

        return $"DeskAI read that as: {understood}. Check it and change anything that is wrong, "
            + $"then give it a name and save it.{problem}{unsupported}";
    }

    /// <summary>
    /// Drops the last answer as soon as the form changes.
    /// </summary>
    /// <remarks>
    /// A refusal that stays on screen while someone fixes the thing it complained about
    /// stops describing anything true, and reads as if the fix did not work.
    /// </remarks>
    private void ClearFormMessage()
    {
        if (!string.IsNullOrEmpty(_formMessage))
        {
            FormMessage = string.Empty;
        }
    }

    private static bool IsExpectedFailure(Exception exception) =>
        exception is IOException
            or InvalidOperationException
            or FormatException
            or System.Data.Common.DbException;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _presence.SettingsChangedOutsideThePage -= OnSettingsChangedOutsideThePage;
    }
}
