using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeskAI.Core.Search;
using DeskAI.Core.Tidy;

namespace DeskAI.App.ViewModels;

/// <summary>A connected folder offered in the folder list.</summary>
public sealed record TidyFolderOption(Guid Id, string Name, string Path, bool CanTidy)
{
    public override string ToString() => Name;
}

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

    public TidyViewModel(
        ConnectedFolderService folders,
        TidyPermissionService permission,
        TidySuggestionService suggestions,
        TidyAiService ai,
        TidyRunService run)
    {
        _folders = folders;
        _permission = permission;
        _suggestions = suggestions;
        _ai = ai;
        _run = run;
        TidyCommand = new AsyncRelayCommand(TidyAsync, () => CanPressTidy);
        UndoCommand = new AsyncRelayCommand(() => UndoLastTidyAsync(), () => CanUndo && !IsTidying);
        StopTidyingCommand = new AsyncRelayCommand(StopTidyingAsync, () => SelectedFolder?.CanTidy == true && !IsBusy);
        RefreshCommand = new AsyncRelayCommand(() => _pending = LoadAsync(), () => SelectedFolder is not null && !IsBusy);
        StopAiCommand = new RelayCommand(() => _aiCancellation?.Cancel(), () => IsAskingAi);
        KeepInterruptedCommand = new AsyncRelayCommand(KeepInterruptedAsync, () => HasInterrupted && !IsTidying);
    }

    /// <summary>"Keep them", or "OK" when there is nothing to put back.</summary>
    public AsyncRelayCommand KeepInterruptedCommand { get; }

    /// <summary>Files DeskAI could not tell about after the interruption, with where to look.</summary>
    public ObservableCollection<TidyLeftAloneViewModel> InterruptedFiles { get; } = [];

    public bool HasInterrupted => _interrupted is not null;
    public bool HasInterruptedFiles => InterruptedFiles.Count > 0;

    public string InterruptedTitle => _interrupted switch
    {
        null => string.Empty,
        { IsUndo: true } undo => $"Your last undo was interrupted: {undo.Moved} of {Files(undo.Total)} went back.",
        { Moved: 0 } => "Your last tidy was interrupted before any file moved.",
        var tidy => $"Your last tidy was interrupted: {tidy.Moved} of {Files(tidy.Total)} moved.",
    };

    public string InterruptedNote => _interrupted switch
    {
        null => string.Empty,
        { IsUndo: true } undo when undo.Moved == undo.Total => "DeskAI checked each file. Every one had gone back.",
        { IsUndo: true } => "DeskAI checked each file. The rest are still where the tidy put them.",
        { Moved: 0 } => "DeskAI checked each file. Nothing needs putting back.",
        _ => "DeskAI checked each file. You can put back the ones that moved, or keep them where they are now.",
    };

    public bool CanUndoInterrupted => _interrupted?.CanUndo == true;

    public string UndoInterruptedText => _interrupted?.Moved == 1 ? "Undo that file" : $"Undo those {_interrupted?.Moved}";

    public string KeepInterruptedText => _interrupted switch
    {
        { CanUndo: true, Moved: 1 } => "Keep it",
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

    public async Task InitializeAsync()
    {
        await ReloadFoldersAsync(selectId: null).ConfigureAwait(true);
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
        if (_interrupted is not { CanUndo: true } interrupted || SelectedFolder is not { } folder || IsTidying)
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
        if (_interrupted is not { } interrupted || SelectedFolder is not { } folder)
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

        if (SelectedFolder is not { CanTidy: true } folder)
        {
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
        }
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
