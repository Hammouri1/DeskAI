using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeskAI.Core.Plans;
using DeskAI.Core.Search;
using DeskAI.Core.Tidy;

namespace DeskAI.App.ViewModels;

/// <summary>A connected folder offered in the folder list.</summary>
public sealed record TidyFolderOption(Guid Id, string Name, string Path, bool CanTidy)
{
    public override string ToString() => Name;
}

/// <summary>What the dialog before "Tidy while I'm away" says: the folder, the rules as worded, the ceiling.</summary>
public sealed record AwayTidyQuestion(string Title, string Intro, IReadOnlyList<string> Rules, string Promise);

/// <summary>A file DeskAI will not touch, with the reason in plain words.</summary>
public sealed record TidyLeftAloneViewModel(string FileName, string Reason);

/// <summary>One suggested file on the Organize page.</summary>
public sealed class TidyItemViewModel : ObservableObject
{
    private readonly Action _selectionChanged;
    private readonly Action<Guid, bool> _keepBothChanged;
    private bool _isIncluded;
    private bool _keepBoth;

    public TidyItemViewModel(TidySuggestion suggestion, Action selectionChanged, Action<Guid, bool> keepBothChanged)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        FileId = suggestion.FileId;
        FileName = suggestion.FileName;
        Reason = suggestion.Reason;
        MoveOperationId = suggestion.MoveOperationId;
        HasSameName = suggestion.HasSameName;
        IsUnsure = suggestion.IsUnsure;
        IsFromAi = suggestion.Source == TidySuggestionSource.Ai;
        Destination = "Goes to " + suggestion.DestinationRelativePath.Replace("\\", " › ", StringComparison.Ordinal);
        _keepBoth = suggestion.Choice == SameNameChoice.KeepBoth;

        // An idea the AI itself was unsure about waits for the person to tick it.
        _isIncluded = MoveOperationId is not null && !IsUnsure;
        _selectionChanged = selectionChanged;
        _keepBothChanged = keepBothChanged;
    }

    public Guid FileId { get; }
    public string FileName { get; }
    public string Reason { get; }
    public string Destination { get; }
    public Guid? MoveOperationId { get; }
    public bool HasSameName { get; }
    public bool IsUnsure { get; }
    public bool IsFromAi { get; }

    /// <summary>A skipped same-name file has nothing to move until "Keep both" is chosen.</summary>
    public bool CanBeIncluded => MoveOperationId is not null;

    public string SameNameMessage => $"A file called {FileName} is already there.";

    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            if (SetProperty(ref _isIncluded, value && CanBeIncluded))
            {
                _selectionChanged();
            }
        }
    }

    public bool KeepBoth
    {
        get => _keepBoth;
        set
        {
            if (SetProperty(ref _keepBoth, value))
            {
                _keepBothChanged(FileId, value);
            }
        }
    }
}

/// <summary>The files that would go into one folder.</summary>
public sealed class TidyGroupViewModel : ObservableObject
{
    public const string UnsureTitle = "AI isn't sure";

    public TidyGroupViewModel(string folder, IEnumerable<TidyItemViewModel> items, bool isUnsure = false)
    {
        Folder = folder;
        IsUnsure = isUnsure;
        DisplayName = isUnsure ? UnsureTitle : folder.Replace("\\", " › ", StringComparison.Ordinal);
        Items = new ObservableCollection<TidyItemViewModel>(items);
    }

    /// <summary>The destination folder, or <see cref="UnsureTitle"/> for the unsure group.</summary>
    public string Folder { get; }
    public string DisplayName { get; }

    /// <summary>
    /// The group of AI ideas the AI was not confident about. Its files go to different folders,
    /// so each row says where, and they all start unticked.
    /// </summary>
    public bool IsUnsure { get; }

    /// <summary>A folder, or a question mark for ideas that need checking.</summary>
    public string Glyph => IsUnsure ? "\uE9CE" : "\uE8B7";

    public string Note => IsUnsure ? "These start unticked. Tick the ones you agree with." : string.Empty;
    public bool HasNote => IsUnsure;
    public ObservableCollection<TidyItemViewModel> Items { get; }

    public int IncludedCount => Items.Count(item => item.IsIncluded);

    public string CountText => Items.Count == 1 ? "1 file" : $"{Items.Count} files";

    /// <summary>True when every movable file is ticked, false when none is, null when mixed.</summary>
    public bool? IsIncluded
    {
        get
        {
            var movable = Items.Where(item => item.CanBeIncluded).ToArray();
            if (movable.Length == 0 || movable.All(item => !item.IsIncluded))
            {
                return false;
            }

            return movable.All(item => item.IsIncluded) ? true : null;
        }
        set
        {
            if (value is bool include)
            {
                foreach (var item in Items)
                {
                    item.IsIncluded = include;
                }
            }

            Refresh();
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsIncluded));
        OnPropertyChanged(nameof(IncludedCount));
    }
}

/// <summary>
/// Drives the Organize page: pick a folder, allow tidying, and see what tidying would do.
/// </summary>
/// <remarks>
/// <para>
/// Pressing Tidy moves exactly the ticked files, each checked again right before it moves,
/// and the result says what happened, including every file that stayed and why. Undo is offered
/// for that tidy and, like tidying, needs the folder's tidy permission. Nothing else on this
/// page changes a file; asking AI only adds suggestions.
/// </para>
/// <para>
/// When a folder is shown, its history is read once: the last tidy, which can still be undone
/// after DeskAI was closed, or a tidy that was interrupted, which is checked against the disk
/// and put to the person as a question. Tidy stays off until that question is answered.
/// </para>
/// </remarks>
public sealed class TidyViewModel : ObservableObject, IDisposable
{
    private readonly ConnectedFolderService _folders;
    private readonly TidyPermissionService _permission;
    private readonly TidySuggestionService _suggestions;
    private readonly TidyAiService _ai;
    private readonly TidyRunService _run;
    private readonly OrganizeRequest _request;
    private readonly Dictionary<Guid, SameNameChoice> _choices = [];

    // What AI said, by file. Kept across reloads of the list so ticking "Keep both" or looking
    // again does not throw away an answer the person may have paid for; each idea still
    // expires by itself if its file changes.
    private readonly Dictionary<Guid, TidyAiAdvice> _aiAdvice = [];
    private TidyFolderOption? _selectedFolder;
    private Guid _planId = Guid.NewGuid();
    private int _revision;
    private bool _isBusy;
    private bool _needsPermission;
    private string _message = string.Empty;
    private string _summaryTitle = string.Empty;
    private string _limitNote = string.Empty;
    private Task _pending = Task.CompletedTask;
    private TidySuggestionMode _mode = TidySuggestionMode.TypesAndRules;
    private TidyAiStatus? _aiStatus;
    private TidyPreview? _preview;
    private bool _isAskingAi;
    private string _aiMessage = string.Empty;
    private CancellationTokenSource? _aiCancellation;
    private TidyRunResult? _lastRun;
    private Guid? _lastRunFolderId;
    private bool _isTidying;
    private string _resultSummary = string.Empty;
    private string _undoSummary = string.Empty;

    // Which folder's history has been read, so it is read once per folder rather than on
    // every reload of the list; and the question about an interrupted tidy, if there is one.
    private Guid? _historyFolderId;
    private InterruptedTidy? _interrupted;
    private string _interruptedMessage = string.Empty;

    // The folder an automatic check's notice asked to open, while it is the one shown.
    private Guid? _reviewFolderId;
    private string _reviewNote = string.Empty;

    private readonly AwayTidyService _away;
    private AwayTidyStatus? _awayStatus;

    public TidyViewModel(
        ConnectedFolderService folders,
        TidyPermissionService permission,
        TidySuggestionService suggestions,
        TidyAiService ai,
        TidyRunService run,
        OrganizeRequest request,
        AwayTidyService away)
    {
        _folders = folders;
        _permission = permission;
        _suggestions = suggestions;
        _ai = ai;
        _run = run;
        _request = request;
        _away = away;
        GotItCommand = new AsyncRelayCommand(GotItAsync, () => HasAwayRuns);
        TidyCommand = new AsyncRelayCommand(TidyAsync, () => CanPressTidy);
        UndoCommand = new AsyncRelayCommand(() => UndoLastTidyAsync(), () => CanUndo && !IsTidying);
        StopTidyingCommand = new AsyncRelayCommand(StopTidyingAsync, () => SelectedFolder?.CanTidy == true && !IsBusy);
        RefreshCommand = new AsyncRelayCommand(() => _pending = LoadAsync(), () => SelectedFolder is not null && !IsBusy);
        StopAiCommand = new RelayCommand(() => _aiCancellation?.Cancel(), () => IsAskingAi);
        KeepInterruptedCommand = new AsyncRelayCommand(KeepInterruptedAsync, () => CanAnswerInterrupted && !IsTidying);
    }

    /// <summary>"Keep them", or "OK" when there is nothing to put back.</summary>
    public AsyncRelayCommand KeepInterruptedCommand { get; }

    // Tidy while I'm away (V0.9, ADR 0031). The switch, its line, and the card of runs the
    // person has not looked at. The status is read from AwayTidyService each time the folder
    // loads, which also turns the mode off with the reason if the rules no longer match the yes.

    /// <summary>The switch is offered only where tidying is already allowed.</summary>
    public bool HasAwaySwitch => SelectedFolder is { CanTidy: true };

    public bool IsAwayOn => _awayStatus?.IsOn == true;

    /// <summary>On when it can be turned on, or is on and so can be turned off.</summary>
    public bool CanUseAwaySwitch => _awayStatus is { } status && (status.CanTurnOn || status.IsOn);

    public string AwayLine => _awayStatus?.Line ?? string.Empty;

    public bool AwayLineIsCaution => _awayStatus?.IsCaution == true;

    public bool AwayLineIsPlain => !AwayLineIsCaution;

    /// <summary>What DeskAI did while nobody was watching, one line per run, never a file name.</summary>
    public ObservableCollection<string> AwayRuns { get; } = [];

    public bool HasAwayRuns => AwayRuns.Count > 0;

    /// <summary>"Got it": the runs have been looked at. Undo, if wanted, is the same Undo as any tidy.</summary>
    public AsyncRelayCommand GotItCommand { get; }

    /// <summary>
    /// What the dialog before the yes must say: the folder, the rules as worded, and the ceiling.
    /// </summary>
    /// <returns>The question, or null when the switch cannot be turned on right now.</returns>
    public async Task<AwayTidyQuestion?> PrepareAwayQuestionAsync()
    {
        if (SelectedFolder is not { CanTidy: true } folder)
        {
            return null;
        }

        var rules = await _away.EnabledRulesAsync().ConfigureAwait(true);
        if (rules.Count == 0)
        {
            return null;
        }

        return new AwayTidyQuestion(
            $"Tidy {folder.Name} while you're away?",
            $"Whenever DeskAI checks your folders — and after you close the window, if you turned that on — it will move loose files in {folder.Name} that these rules match, into folders inside {folder.Name}:",
            rules.Select(rule => rule.Describe()).ToArray(),
            $"At most {AwayTidyLimits.MaxFilesPerRun} files each time. It stops and waits for you if a rule changes, a file is in the way, "
                + $"or a file can't be moved. It never deletes anything and never moves a file out of {folder.Name}. You can undo every run here.");
    }

    /// <summary>The dialog's yes. Records the rules as they are now.</summary>
    public async Task TurnAwayOnAsync()
    {
        if (SelectedFolder is not { } folder)
        {
            return;
        }

        try
        {
            ShowAway(await _away.TurnOnAsync(folder.Id).ConfigureAwait(true));
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI could not turn that on: {exception.Message}";
        }
    }

    public async Task TurnAwayOffAsync()
    {
        if (SelectedFolder is not { } folder)
        {
            return;
        }

        try
        {
            ShowAway(await _away.TurnOffAsync(folder.Id).ConfigureAwait(true));
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI could not turn that off: {exception.Message}";
        }
    }

    private async Task GotItAsync()
    {
        if (SelectedFolder is not { } folder)
        {
            return;
        }

        try
        {
            await _away.MarkSeenAsync(folder.Id).ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI could not note that: {exception.Message}";
            return;
        }

        AwayRuns.Clear();
        OnPropertyChanged(nameof(HasAwayRuns));
        GotItCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadAwayAsync(TidyFolderOption folder)
    {
        try
        {
            var status = await _away.GetStatusAsync(folder.Id).ConfigureAwait(true);
            var runs = await _away.ListUnseenAsync(folder.Id).ConfigureAwait(true);
            if (SelectedFolder?.Id != folder.Id)
            {
                return;
            }

            ShowAway(status);
            AwayRuns.Clear();
            foreach (var run in runs)
            {
                var when = run.RanAtUtc.ToLocalTime();
                AwayRuns.Add(run.Moved > 0
                    ? $"While you were away, DeskAI tidied {Files(run.Moved)} into {FolderCount(run.FoldersUsed)} at {when:t} on {when:d}."
                        + (run.StoppedReason is { } then ? $" Then it stopped: {then}" : string.Empty)
                    : $"DeskAI stopped tidying while you're away at {when:t} on {when:d}: {run.StoppedReason}");
            }

            OnPropertyChanged(nameof(HasAwayRuns));
            GotItCommand.NotifyCanExecuteChanged();
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI could not read the away setting: {exception.Message}";
        }
    }

    private void ShowAway(AwayTidyStatus? status)
    {
        _awayStatus = status;
        OnPropertyChanged(nameof(HasAwaySwitch));
        OnPropertyChanged(nameof(IsAwayOn));
        OnPropertyChanged(nameof(CanUseAwaySwitch));
        OnPropertyChanged(nameof(AwayLine));
        OnPropertyChanged(nameof(AwayLineIsCaution));
        OnPropertyChanged(nameof(AwayLineIsPlain));
    }

    /// <summary>Files DeskAI could not tell about after the interruption, with where to look.</summary>
    public ObservableCollection<TidyLeftAloneViewModel> InterruptedFiles { get; } = [];

    public bool HasInterrupted => _interrupted is not null;
    public bool HasInterruptedFiles => InterruptedFiles.Count > 0;

    public string InterruptedTitle => _interrupted switch
    {
        null => string.Empty,
        { Purpose: not PlanPurpose.Tidy } => "A Desktop Studio change on this folder stopped part-way.",
        { IsFolders: true, IsUndo: true } undo => $"Your last undo was interrupted: {undo.MadeFolders.Count} of {FolderCount(undo.TotalFolders)} removed.",
        { IsFolders: true } folders => $"DeskAI stopped while making folders: {folders.MadeFolders.Count} of {FolderCount(folders.TotalFolders)} made.",
        { IsUndo: true } undo => $"Your last undo was interrupted: {undo.Moved} of {Files(undo.Total)} went back.",
        { Moved: 0 } => "Your last tidy was interrupted before any file moved.",
        var tidy => $"Your last tidy was interrupted: {tidy.Moved} of {Files(tidy.Total)} moved.",
    };

    public string InterruptedNote => _interrupted switch
    {
        null => string.Empty,
        { Purpose: not PlanPurpose.Tidy } => "Answer it in Desktop Studio. Until then, DeskAI won't tidy this folder.",
        { IsFolders: true, IsUndo: true } => "DeskAI checked each folder. A folder that is still there was left in place.",
        { IsFolders: true, CanUndo: false } => "DeskAI checked each folder. Nothing needs removing.",
        { IsFolders: true } => "DeskAI checked each folder. You can remove the empty folders it made, or keep them.",
        { IsUndo: true } undo when undo.Moved == undo.Total => "DeskAI checked each file. Every one had gone back.",
        { IsUndo: true } => "DeskAI checked each file. The rest are still where the tidy put them.",
        { Moved: 0 } => "DeskAI checked each file. Nothing needs putting back.",
        _ => "DeskAI checked each file. You can put back the ones that moved, or keep them where they are now.",
    };

    public bool CanUndoInterrupted => _interrupted is { CanUndo: true, Purpose: PlanPurpose.Tidy };

    /// <summary>Only a tidy is answered here; a Desktop Studio change is answered there, with its own permission (ADR 0044).</summary>
    public bool CanAnswerInterrupted => _interrupted is { Purpose: PlanPurpose.Tidy };

    public string UndoInterruptedText => _interrupted switch
    {
        { IsFolders: true, MadeFolders.Count: 1 } => "Remove that folder",
        { IsFolders: true } folders => $"Remove those {folders.MadeFolders.Count} folders",
        { Moved: 1 } => "Undo that file",
        var tidy => $"Undo those {tidy?.Moved}",
    };

    public string KeepInterruptedText => _interrupted switch
    {
        { CanUndo: true, IsFolders: true, MadeFolders.Count: 1 } => "Keep it",
        { CanUndo: true, IsFolders: false, Moved: 1 } => "Keep it",
        { CanUndo: true } => "Keep them",
        _ => "OK",
    };

    /// <summary>The answer to pressing one of the two buttons, when it did not go through.</summary>
    public string InterruptedMessage
    {
        get => _interruptedMessage;
        private set
        {
            if (SetProperty(ref _interruptedMessage, value))
            {
                OnPropertyChanged(nameof(HasInterruptedMessage));
            }
        }
    }

    public bool HasInterruptedMessage => !string.IsNullOrEmpty(InterruptedMessage);

    public ObservableCollection<TidyFolderOption> Folders { get; } = [];
    public ObservableCollection<TidyGroupViewModel> Groups { get; } = [];
    public ObservableCollection<TidyLeftAloneViewModel> LeftAlone { get; } = [];

    public AsyncRelayCommand StopTidyingCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand StopAiCommand { get; }
    public AsyncRelayCommand TidyCommand { get; }
    public AsyncRelayCommand UndoCommand { get; }

    /// <summary>Files the last tidy or undo did not move, each with its reason.</summary>
    public ObservableCollection<TidyLeftAloneViewModel> ResultSkipped { get; } = [];

    /// <summary>
    /// 0 for "DeskAI and my rules", 1 for "Ask AI about every file". Choosing 1 is ignored
    /// while AI is not set up, because the page shows that choice switched off.
    /// </summary>
    public int SuggestionModeIndex
    {
        get => (int)_mode;
        set
        {
            var mode = value == 1 && CanUseAiForEveryFile
                ? TidySuggestionMode.AiForEveryFile
                : TidySuggestionMode.TypesAndRules;
            if (mode != _mode)
            {
                _mode = mode;
                OnPropertyChanged();
                _pending = LoadAsync();
            }
            else if (value != (int)_mode)
            {
                // Tell the page its radio button was not accepted, so it springs back.
                OnPropertyChanged();
            }
        }
    }

    public bool CanUseAiForEveryFile => _aiStatus?.IsSetUp == true && _aiStatus.CanShareAnything;

    /// <summary>The AI card appears once a folder that may be tidied has been looked at.</summary>
    public bool HasAiPanel => _preview is { CanTidy: true, FolderProblem: null } && !NeedsPermission;

    public int AskableCount => _preview?.AskableFiles.Count ?? 0;

    public bool HasAskable => AskableCount > 0;

    public bool CanAskAi => CanUseAiForEveryFile && AskableCount > 0 && !IsAskingAi && !IsBusy;

    public string AskAiText => AskableCount switch
    {
        > TidyAiService.MaxFilesPerRequest => $"Ask AI about the first {TidyAiService.MaxFilesPerRequest} files",
        1 => "Ask AI about 1 file",
        var count => $"Ask AI about {count} files",
    };

    /// <summary>
    /// "Plan this folder with AI" (ADR 0034): AI names folders and says which file goes where, for
    /// every file the person's rules do not place. Same sharing rules, same preview, same Tidy.
    /// </summary>
    public bool CanPlanAi => CanUseAiForEveryFile && HasAiPanel && !IsAskingAi && !IsBusy;

    public static string PlanAiText => "Plan this folder with AI";

    public static string PlanAiPrompt =>
        "Or let AI plan the whole folder: it names folders and says which file goes into which. "
        + "DeskAI checks every name, and you still see the whole plan before anything moves.";

    /// <summary>What there is to ask about, in one line, before anything is pressed.</summary>
    public string AskAiPrompt => (AskableCount, _mode) switch
    {
        (0, _) when _aiAdvice.Count > 0 => "AI has given its ideas. They are in the list above.",
        (0, TidySuggestionMode.TypesAndRules) => "DeskAI knows where every file here goes, so there is nothing to ask AI.",
        (0, _) => "Your rules already place every file here, so there is nothing to ask AI.",
        (1, TidySuggestionMode.TypesAndRules) => "DeskAI doesn't know where 1 file goes. AI can suggest a place.",
        (var count, TidySuggestionMode.TypesAndRules) => $"DeskAI doesn't know where {count} files go. AI can suggest a place.",
        (1, _) => "AI can suggest a place for 1 file your rules don't place.",
        (var count, _) => $"AI can suggest a place for {count} files your rules don't place.",
    };

    /// <summary>Who would see what, or how to turn AI on. Shown before the button is pressed.</summary>
    public string AiSharingNote => _aiStatus?.Explanation ?? string.Empty;

    public bool IsAskingAi
    {
        get => _isAskingAi;
        private set
        {
            if (SetProperty(ref _isAskingAi, value))
            {
                RaiseAiChanges();
            }
        }
    }

    /// <summary>The answer to pressing Ask, shown beside the button.</summary>
    public string AiMessage
    {
        get => _aiMessage;
        private set
        {
            if (SetProperty(ref _aiMessage, value))
            {
                OnPropertyChanged(nameof(HasAiMessage));
            }
        }
    }

    public bool HasAiMessage => !string.IsNullOrEmpty(AiMessage);

    public string SuggestionsFromText => Groups.Any(group => group.Items.Any(item => item.IsFromAi))
        ? "Suggestions from: DeskAI, your rules, and AI"
        : "Suggestions from: DeskAI and your rules";

    public TidyFolderOption? SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (SetProperty(ref _selectedFolder, value))
            {
                // The last result belongs to its folder. Re-selecting the same folder, as
                // allowing tidying again does, keeps it so Undo stays one press away.
                if (value?.Id != _lastRunFolderId)
                {
                    ClearResult();
                }

                // Its history, likewise, is read again only for a different folder.
                if (value?.Id != _historyFolderId)
                {
                    _historyFolderId = null;
                    SetInterrupted(null);
                }

                _choices.Clear();
                _aiAdvice.Clear();
                AiMessage = string.Empty;
                _planId = Guid.NewGuid();
                _revision = 0;
                OnPropertyChanged(nameof(PermissionTitle));
                OnPropertyChanged(nameof(CanStopTidying));
                StopTidyingCommand.NotifyCanExecuteChanged();
                RefreshCommand.NotifyCanExecuteChanged();
                _pending = LoadAsync();
            }
        }
    }

    public bool HasFolders => Folders.Count > 0;
    public bool HasNoFolders => Folders.Count == 0;
    public bool HasSuggestions => Groups.Count > 0;
    public bool HasLeftAlone => LeftAlone.Count > 0;
    public bool HasMessage => !string.IsNullOrEmpty(Message);
    public bool CanStopTidying => SelectedFolder?.CanTidy == true;
    public string LeftAloneTitle => LeftAlone.Count == 1 ? "Left alone (1 file)" : $"Left alone ({LeftAlone.Count} files)";
    public int IncludedCount => Groups.Sum(group => group.IncludedCount);
    public string TidyButtonText => IncludedCount == 1 ? "Tidy 1 file" : $"Tidy {IncludedCount} files";

    /// <summary>
    /// Ticked files, a folder that may be tidied, nothing else running, and no open question
    /// about an interrupted tidy.
    /// </summary>
    public bool CanPressTidy =>
        IncludedCount > 0 && _preview is { CanTidy: true } && SelectedFolder?.CanTidy == true && !IsBusy && !IsTidying &&
        !IsAskingAi && !HasInterrupted;

    public string TidyNote => HasInterrupted
        ? "Answer the question about your last tidy first."
        : "Nothing moves until you press it. You can undo it.";

    public bool IsTidying
    {
        get => _isTidying;
        private set
        {
            if (SetProperty(ref _isTidying, value))
            {
                OnPropertyChanged(nameof(CanPressTidy));
                TidyCommand.NotifyCanExecuteChanged();
                UndoCommand.NotifyCanExecuteChanged();
                KeepInterruptedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>What the last press of Tidy did, in one line.</summary>
    public string ResultSummary
    {
        get => _resultSummary;
        private set
        {
            if (SetProperty(ref _resultSummary, value))
            {
                OnPropertyChanged(nameof(HasResult));
            }
        }
    }

    public bool HasResult => !string.IsNullOrEmpty(ResultSummary);
    public bool HasResultSkipped => ResultSkipped.Count > 0;

    /// <summary>The answer to pressing Undo, shown under the Undo button.</summary>
    public string UndoSummary
    {
        get => _undoSummary;
        private set
        {
            if (SetProperty(ref _undoSummary, value))
            {
                OnPropertyChanged(nameof(HasUndoSummary));
            }
        }
    }

    public bool HasUndoSummary => !string.IsNullOrEmpty(UndoSummary);

    public bool CanUndo => _lastRun?.CanUndo == true;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                StopTidyingCommand.NotifyCanExecuteChanged();
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool NeedsPermission
    {
        get => _needsPermission;
        private set => SetProperty(ref _needsPermission, value);
    }

    public string PermissionTitle => SelectedFolder is null ? string.Empty : $"Allow DeskAI to tidy {SelectedFolder.Name}?";

    public string Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
            }
        }
    }

    public string SummaryTitle
    {
        get => _summaryTitle;
        private set => SetProperty(ref _summaryTitle, value);
    }

    public string LimitNote
    {
        get => _limitNote;
        private set
        {
            if (SetProperty(ref _limitNote, value))
            {
                OnPropertyChanged(nameof(HasLimitNote));
            }
        }
    }

    public bool HasLimitNote => !string.IsNullOrEmpty(LimitNote);

    /// <summary>Lets tests wait for the reload a property change started.</summary>
    public Task WhenIdleAsync() => _pending;

    private static readonly string[] Steps =
    [
        "Pick a folder, or add one with \"Choose inside your folders\".",
        "Allow tidying for that folder. You can take it back at any time.",
        "Look through the suggestions. Untick anything you want to leave where it is.",
        "Press Tidy. Changed your mind? Press Undo, even after closing DeskAI.",
    ];

    private bool _howItWorksOpen;
    private bool _howItWorksDecided;

    /// <summary>
    /// The "How tidying works" card, which replaced the practice page on 2026-09-11: four
    /// short steps, in order.
    /// </summary>
    public IReadOnlyList<string> HowItWorksSteps { get; } = Steps;

    public string HowItWorksPromise { get; } =
        "DeskAI never deletes anything, never touches files in subfolders, and never moves anything out of the folder you picked.";

    /// <summary>
    /// Open the first time the page is shown to someone who has not allowed tidying anywhere
    /// yet; closed for everyone else. After that it stays however the person left it.
    /// </summary>
    public bool HowItWorksOpen
    {
        get => _howItWorksOpen;
        set => SetProperty(ref _howItWorksOpen, value);
    }

    /// <summary>Said above the list when an automatic check's notice opened this folder.</summary>
    public string ReviewNote
    {
        get => _reviewNote;
        private set
        {
            if (SetProperty(ref _reviewNote, value))
            {
                OnPropertyChanged(nameof(HasReviewNote));
            }
        }
    }

    public bool HasReviewNote => !string.IsNullOrEmpty(ReviewNote);

    /// <summary>
    /// Opens on the folder "Review in Organize" asked for, if there is one and it is still
    /// connected; otherwise on the first folder, as always.
    /// </summary>
    public async Task InitializeAsync()
    {
        _reviewFolderId = _request.Take();
        await ReloadFoldersAsync(selectId: _reviewFolderId).ConfigureAwait(true);
        await _pending.ConfigureAwait(true);
    }

    /// <summary>Connects a folder the person picked and confirmed, then selects it.</summary>
    public async Task ConnectAndSelectAsync(string path)
    {
        IsBusy = true;
        try
        {
            var result = await _folders.ConnectAsync(path).ConfigureAwait(true);
            if (!result.IsAllowed || result.Folder is null)
            {
                Message = result.Explanation;
                return;
            }

            Message = string.Empty;
            IsBusy = false;
            await ReloadFoldersAsync(result.Folder.Id).ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        await _pending.ConfigureAwait(true);
    }

    /// <summary>Called only after the page's permission dialog was accepted.</summary>
    public async Task AllowTidyAsync()
    {
        if (SelectedFolder is not { } folder)
        {
            return;
        }

        var result = await _permission.AllowAsync(folder.Id).ConfigureAwait(true);
        Message = result.IsAllowed ? string.Empty : result.Explanation;
        await ReloadFoldersAsync(folder.Id).ConfigureAwait(true);
        await _pending.ConfigureAwait(true);
    }

    private async Task StopTidyingAsync()
    {
        if (SelectedFolder is not { } folder)
        {
            return;
        }

        var result = await _permission.StopAsync(folder.Id).ConfigureAwait(true);
        Message = result.Explanation;
        await ReloadFoldersAsync(folder.Id).ConfigureAwait(true);
        await _pending.ConfigureAwait(true);
    }

    private async Task ReloadFoldersAsync(Guid? selectId)
    {
        var keep = selectId ?? SelectedFolder?.Id;
        var connected = await _folders.ListAsync().ConfigureAwait(true);
        Folders.Clear();
        foreach (var folder in connected)
        {
            Folders.Add(new TidyFolderOption(folder.Id, folder.Name, folder.Path, folder.CanTidy));
        }

        OnPropertyChanged(nameof(HasFolders));
        OnPropertyChanged(nameof(HasNoFolders));
        if (!_howItWorksDecided)
        {
            _howItWorksDecided = true;
            HowItWorksOpen = !Folders.Any(folder => folder.CanTidy);
        }

        // Opening the page with folders already connected shows the first one straight away,
        // rather than an empty page that makes someone hunt for what to press.
        var next = Folders.FirstOrDefault(item => item.Id == keep) ?? Folders.FirstOrDefault();

        // Re-selecting the same folder must still reload, because its permission may have
        // just changed; clearing first makes the assignment below count as a change.
        _selectedFolder = null;
        SelectedFolder = next;
        if (next is null)
        {
            OnPropertyChanged(nameof(SelectedFolder));
            _pending = LoadAsync();
        }
    }

    /// <summary>
    /// Builds the question for AI and returns it for the page to show. Sends nothing.
    /// </summary>
    /// <returns>The question to show, or null with the reason in <see cref="AiMessage"/>.</returns>
    public async Task<TidyAiQuestion?> PrepareAiQuestionAsync()
    {
        if (SelectedFolder is not { } folder || _preview is null || !CanAskAi)
        {
            return null;
        }

        try
        {
            var prepared = await _ai.PrepareAsync(folder.Id, _preview.AskableFiles).ConfigureAwait(true);
            AiMessage = prepared.Question is null ? prepared.Explanation : string.Empty;
            return prepared.Question;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            AiMessage = $"AI stayed off: {exception.Message}";
            return null;
        }
    }

    /// <summary>
    /// Builds the "plan this folder" question for AI and returns it for the page to show. Sends
    /// nothing. Switches the page to "every file my rules don't place" first, because a plan is
    /// about the whole folder, not only the files DeskAI cannot place by type.
    /// </summary>
    /// <returns>The question to show, or null with the reason in <see cref="AiMessage"/>.</returns>
    public async Task<TidyAiQuestion?> PreparePlanQuestionAsync()
    {
        if (SelectedFolder is not { } folder || _preview is null || !CanPlanAi)
        {
            return null;
        }

        if (_mode != TidySuggestionMode.AiForEveryFile)
        {
            SuggestionModeIndex = 1;
            await _pending.ConfigureAwait(true);
        }

        if (_preview is null || _preview.AskableFiles.Count == 0)
        {
            AiMessage = "Your rules already place every file here, so there is nothing for AI to plan.";
            return null;
        }

        try
        {
            var prepared = await _ai.PrepareAsync(folder.Id, _preview.AskableFiles, planFolder: true).ConfigureAwait(true);
            AiMessage = prepared.Question is null ? prepared.Explanation : string.Empty;
            return prepared.Question;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            AiMessage = $"AI stayed off: {exception.Message}";
            return null;
        }
    }

    /// <summary>
    /// Sends the question the person just saw, after they pressed Send in the page's dialog.
    /// </summary>
    public async Task AskAiAsync(TidyAiQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (IsAskingAi || SelectedFolder?.Id != question.RootId)
        {
            return;
        }

        IsAskingAi = true;
        var cancellation = new CancellationTokenSource();
        _aiCancellation = cancellation;
        AiMessage = $"Asking {question.ServiceName}…";
        try
        {
            var answer = await _ai.AskAsync(question, cancellation.Token).ConfigureAwait(true);
            foreach (var (fileId, advice) in answer.Advice)
            {
                _aiAdvice[fileId] = advice;
            }

            AiMessage = answer.Message;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            AiMessage = $"AI stayed off: {exception.Message}";
        }
        finally
        {
            // Leaving the page may already have disposed it; disposing twice is harmless.
            cancellation.Dispose();
            if (ReferenceEquals(_aiCancellation, cancellation))
            {
                _aiCancellation = null;
            }

            IsAskingAi = false;
        }

        _pending = LoadAsync();
        await _pending.ConfigureAwait(true);
    }

    /// <summary>
    /// Moves exactly the ticked files. Each is checked again right before it moves; any that
    /// are not safe to move stay where they are and are listed with the reason.
    /// </summary>
    private async Task TidyAsync()
    {
        if (!CanPressTidy || _preview is not { } preview || SelectedFolder is not { } folder)
        {
            return;
        }

        var ticked = Groups
            .SelectMany(group => group.Items)
            .Where(item => item.IsIncluded && item.MoveOperationId is not null)
            .Select(item => item.MoveOperationId!.Value)
            .ToArray();
        IsTidying = true;
        ClearResult();
        try
        {
            var result = await _run.TidyAsync(preview, ticked).ConfigureAwait(true);
            _lastRun = result;
            _lastRunFolderId = folder.Id;
            foreach (var skipped in result.Skipped)
            {
                ResultSkipped.Add(new TidyLeftAloneViewModel(skipped.FileName, skipped.Reason));
            }

            ResultSummary = result.Summary;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            ResultSummary = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsTidying = false;
            RaiseResultChanges();
        }

        // Moved files now sit in folders, so the list is worked out again from the disk.
        _choices.Clear();
        _pending = LoadAsync();
        await _pending.ConfigureAwait(true);
    }

    /// <summary>
    /// Puts back what the last tidy moved. Returns the result so the page can ask to allow
    /// tidying again when undo was refused for want of that permission.
    /// </summary>
    public async Task<TidyUndoResult?> UndoLastTidyAsync()
    {
        if (_lastRun is not { TransactionId: { } transactionId } run || _lastRunFolderId is not { } folderId || IsTidying)
        {
            return null;
        }

        IsTidying = true;
        UndoSummary = string.Empty;
        TidyUndoResult result;
        try
        {
            result = await _run.UndoAsync(folderId, transactionId, run.MovedFiles).ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            result = new TidyUndoResult(false, false, 0, [], $"DeskAI stopped safely: {exception.Message}");
        }
        finally
        {
            IsTidying = false;
        }

        UndoSummary = result.Summary;
        if (result.Finished)
        {
            // An undo is done once; the Undo button goes away and what stayed is listed.
            _lastRun = null;
            ResultSkipped.Clear();
            foreach (var item in result.NotRestored)
            {
                ResultSkipped.Add(new TidyLeftAloneViewModel(item.FileName, item.Reason));
            }

            RaiseResultChanges();
            _pending = LoadAsync();
            await _pending.ConfigureAwait(true);
        }

        return result;
    }

    /// <summary>
    /// "Undo those": puts back the files the interrupted tidy moved. Returns the result so the
    /// page can ask to allow tidying again when that permission is missing; the question stays
    /// open until the undo actually runs.
    /// </summary>
    public async Task<TidyUndoResult?> UndoInterruptedAsync()
    {
        if (_interrupted is not { CanUndo: true, Purpose: PlanPurpose.Tidy } interrupted || SelectedFolder is not { } folder || IsTidying)
        {
            return null;
        }

        IsTidying = true;
        InterruptedMessage = string.Empty;
        TidyUndoResult result;
        try
        {
            result = await _run.UndoInterruptedAsync(folder.Id, interrupted).ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            result = new TidyUndoResult(false, false, 0, [], $"DeskAI stopped safely: {exception.Message}");
        }
        finally
        {
            IsTidying = false;
        }

        if (!result.Finished)
        {
            InterruptedMessage = result.Summary;
            return result;
        }

        // Answered. The question goes, and the result card says what the undo did.
        SetInterrupted(null);
        ClearResult();
        _lastRunFolderId = folder.Id;
        ResultSummary = result.Summary;
        foreach (var item in result.NotRestored)
        {
            ResultSkipped.Add(new TidyLeftAloneViewModel(item.FileName, item.Reason));
        }

        RaiseResultChanges();
        _pending = LoadAsync();
        await _pending.ConfigureAwait(true);
        return result;
    }

    /// <summary>"Keep them" or "OK": what moved stays, and becomes the folder's last tidy.</summary>
    private async Task KeepInterruptedAsync()
    {
        if (_interrupted is not { Purpose: PlanPurpose.Tidy } interrupted || SelectedFolder is not { } folder)
        {
            return;
        }

        IsTidying = true;
        InterruptedMessage = string.Empty;
        string? problem;
        try
        {
            problem = await _run.KeepInterruptedAsync(folder.Id, interrupted.TransactionId).ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            problem = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsTidying = false;
        }

        if (problem is not null)
        {
            InterruptedMessage = problem;
            return;
        }

        // Read the history again: the kept tidy is now the last tidy, and can still be undone.
        SetInterrupted(null);
        _historyFolderId = null;
        _pending = LoadAsync();
        await _pending.ConfigureAwait(true);
    }

    /// <summary>
    /// Reads a folder's history once: a tidy that was interrupted comes first, as a question;
    /// otherwise the last tidy is shown with Undo, unless this visit already has a result.
    /// </summary>
    private async Task LoadHistoryAsync(TidyFolderOption folder)
    {
        _historyFolderId = folder.Id;
        try
        {
            var interrupted = await _run.FindInterruptedAsync(folder.Id).ConfigureAwait(true);
            var last = interrupted is null && !HasResult
                ? await _run.FindLastAsync(folder.Id).ConfigureAwait(true)
                : null;
            if (SelectedFolder?.Id != folder.Id)
            {
                return;
            }

            SetInterrupted(interrupted);
            if (last is not null)
            {
                _lastRun = new TidyRunResult(
                    last.TransactionId, last.Moved, last.Moved, last.FoldersUsed, [], last.MovedFiles, string.Empty);
                _lastRunFolderId = folder.Id;
                var when = last.FinishedAtUtc.ToLocalTime();
                ResultSummary = $"Last tidy: {Files(last.Moved)} tidied into {FolderCount(last.FoldersUsed)}, at {when:t} on {when:d}.";
                RaiseResultChanges();
            }
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI could not read what it did here before: {exception.Message}";
        }
    }

    private void SetInterrupted(InterruptedTidy? interrupted)
    {
        _interrupted = interrupted;
        InterruptedMessage = string.Empty;
        InterruptedFiles.Clear();
        foreach (var file in interrupted?.NeedsReview ?? [])
        {
            InterruptedFiles.Add(new TidyLeftAloneViewModel(file.FileName, file.Reason));
        }

        OnPropertyChanged(nameof(HasInterrupted));
        OnPropertyChanged(nameof(HasInterruptedFiles));
        OnPropertyChanged(nameof(InterruptedTitle));
        OnPropertyChanged(nameof(InterruptedNote));
        OnPropertyChanged(nameof(CanUndoInterrupted));
        OnPropertyChanged(nameof(CanAnswerInterrupted));
        OnPropertyChanged(nameof(UndoInterruptedText));
        OnPropertyChanged(nameof(KeepInterruptedText));
        OnPropertyChanged(nameof(TidyNote));
        OnPropertyChanged(nameof(CanPressTidy));
        TidyCommand.NotifyCanExecuteChanged();
        KeepInterruptedCommand.NotifyCanExecuteChanged();
    }

    private static string Files(int count) => count == 1 ? "1 file" : $"{count} files";

    private static string FolderCount(int count) => count == 1 ? "1 folder" : $"{count} folders";

    private void ClearResult()
    {
        _lastRun = null;
        _lastRunFolderId = null;
        ResultSkipped.Clear();
        ResultSummary = string.Empty;
        UndoSummary = string.Empty;
        RaiseResultChanges();
    }

    private void RaiseResultChanges()
    {
        OnPropertyChanged(nameof(HasResultSkipped));
        OnPropertyChanged(nameof(CanUndo));
        UndoCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadAsync()
    {
        Groups.Clear();
        LeftAlone.Clear();
        LimitNote = string.Empty;
        SummaryTitle = string.Empty;
        _preview = null;
        NeedsPermission = SelectedFolder is { CanTidy: false };
        RaiseListChanges();

        // Read even without the tidy permission: the last tidy and an interrupted one are the
        // person's to see, and Undo asks for the permission when pressed.
        if (SelectedFolder is { } shown && _historyFolderId != shown.Id)
        {
            await LoadHistoryAsync(shown).ConfigureAwait(true);
        }

        // The away switch and its runs, every time: the status check is what turns the mode off
        // when a rule changed, and the person must see that the moment they look.
        if (SelectedFolder is { } shownAway)
        {
            await LoadAwayAsync(shownAway).ConfigureAwait(true);
        }
        else
        {
            ShowAway(null);
            AwayRuns.Clear();
            OnPropertyChanged(nameof(HasAwayRuns));
        }

        if (SelectedFolder is not { CanTidy: true } folder)
        {
            UpdateReviewNote();
            return;
        }

        IsBusy = true;
        try
        {
            // Read fresh each time: the person may have changed their AI choice in Privacy and
            // AI since this page opened, and the page must never offer what is now off.
            _aiStatus = await _ai.GetStatusAsync().ConfigureAwait(true);
            if (!CanUseAiForEveryFile && _mode == TidySuggestionMode.AiForEveryFile)
            {
                _mode = TidySuggestionMode.TypesAndRules;
                OnPropertyChanged(nameof(SuggestionModeIndex));
            }

            var preview = await _suggestions.PreviewAsync(folder.Id, _planId, ++_revision, _choices, _mode, _aiAdvice)
                .ConfigureAwait(true);
            if (preview is null)
            {
                Message = "That folder is no longer connected.";
                return;
            }

            if (preview.FolderProblem is { } problem)
            {
                Message = problem;
                return;
            }

            _preview = preview;
            Apply(preview);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
            RaiseListChanges();
            UpdateReviewNote();
        }
    }

    /// <summary>
    /// When an automatic check's notice opened this folder, says what the person's rules place
    /// here. A check looks at every remembered file, but tidying moves only loose files at the
    /// top of the folder, so the check's own number is not repeated as if it were this list's.
    /// </summary>
    private void UpdateReviewNote()
    {
        if (SelectedFolder is not { } folder || folder.Id != _reviewFolderId)
        {
            ReviewNote = string.Empty;
            return;
        }

        if (NeedsPermission)
        {
            ReviewNote = "Your automatic check found files here that match your rules. Allow tidying to see which ones DeskAI would move.";
            return;
        }

        var placed = _preview?.Suggestions.Count(item => item.Source == TidySuggestionSource.Rule) ?? 0;
        ReviewNote = placed switch
        {
            _ when _preview is null => string.Empty,
            0 => "From your automatic check: the files your rules matched are not loose at the top of this folder, so there is nothing of theirs to tidy here.",
            _ => $"From your automatic check: your rules place {Files(placed)} here, marked \"Your rule\". Nothing moves until you press Tidy.",
        };
    }

    private void Apply(TidyPreview preview)
    {
        // Ideas the AI was unsure about are gathered at the end rather than mixed into the
        // folders they would go to, so they can be checked together.
        foreach (var group in preview.Suggestions
                     .Where(item => !item.IsUnsure)
                     .GroupBy(item => item.DestinationFolder, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            Groups.Add(new TidyGroupViewModel(
                group.Key,
                group.Select(item => new TidyItemViewModel(item, OnSelectionChanged, OnKeepBothChanged))));
        }

        var unsure = preview.Suggestions.Where(item => item.IsUnsure).ToArray();
        if (unsure.Length > 0)
        {
            Groups.Add(new TidyGroupViewModel(
                TidyGroupViewModel.UnsureTitle,
                unsure.Select(item => new TidyItemViewModel(item, OnSelectionChanged, OnKeepBothChanged)),
                isUnsure: true));
        }

        foreach (var item in preview.LeftAlone.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase))
        {
            LeftAlone.Add(new TidyLeftAloneViewModel(item.FileName, item.Explanation));
        }

        SummaryTitle = preview.Suggestions.Count switch
        {
            0 => "Nothing to tidy right now",
            1 => "1 loose file could be tidied",
            var count => $"{count} loose files could be tidied",
        };
        LimitNote = preview.ReachedLimit
            ? $"Showing the first {TidySuggestionService.MaxFilesPerTidy}. Tidy these, then look again for the rest."
            : string.Empty;
    }

    private void OnSelectionChanged()
    {
        foreach (var group in Groups)
        {
            group.Refresh();
        }

        OnPropertyChanged(nameof(IncludedCount));
        OnPropertyChanged(nameof(TidyButtonText));
        OnPropertyChanged(nameof(CanPressTidy));
        TidyCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Changing a same-name choice changes the plan, so the list is worked out again.</summary>
    private void OnKeepBothChanged(Guid fileId, bool keepBoth)
    {
        _choices[fileId] = keepBoth ? SameNameChoice.KeepBoth : SameNameChoice.Skip;
        _pending = LoadAsync();
    }

    private void RaiseListChanges()
    {
        OnPropertyChanged(nameof(HasSuggestions));
        OnPropertyChanged(nameof(HasLeftAlone));
        OnPropertyChanged(nameof(LeftAloneTitle));
        OnPropertyChanged(nameof(CanStopTidying));
        OnPropertyChanged(nameof(SuggestionsFromText));
        RaiseAiChanges();
        OnSelectionChanged();
    }

    private void RaiseAiChanges()
    {
        OnPropertyChanged(nameof(HasAiPanel));
        OnPropertyChanged(nameof(CanUseAiForEveryFile));
        OnPropertyChanged(nameof(HasAskable));
        OnPropertyChanged(nameof(AskableCount));
        OnPropertyChanged(nameof(CanAskAi));
        OnPropertyChanged(nameof(CanPlanAi));
        OnPropertyChanged(nameof(AskAiText));
        OnPropertyChanged(nameof(AskAiPrompt));
        OnPropertyChanged(nameof(AiSharingNote));
        StopAiCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanPressTidy));
        TidyCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Leaving the page stops a request that is still waiting for an answer.</summary>
    public void Dispose()
    {
        _aiCancellation?.Cancel();
        _aiCancellation?.Dispose();
        _aiCancellation = null;
    }

    private static bool IsExpectedFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.Data.Common.DbException;
}
