using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeskAI.App.Preview;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Execution;
using DeskAI.Core.Ai;
using DeskAI.Core.Files;
using DeskAI.Core.Plans;
using DeskAI.Infrastructure.Execution;

namespace DeskAI.App.ViewModels;

public sealed class OrganizeViewModel : ObservableObject, IDisposable
{
    private readonly DemoOrganizationPlanFactory _demoPlanFactory;
    private readonly TemporaryDemoPlanExecutor _executor;
    private readonly IOperationJournal _journal;
    private readonly IReadOnlyFolderService _readOnlyFolderService;
    private readonly IAiSettingsRepository _aiSettingsRepository;
    private readonly IOrganizationSuggestionProvider _aiProvider;
    private OrganizationPlan? _plan;
    private string _demoRoot = "Not created — DeskAI will generate a unique folder under Windows Temp";
    private string _resultMessage = "Nothing has run yet. Review the selected demo actions below.";
    private bool _hasExecuted;
    private bool _isBusy;
    private int _revision;
    private Guid[] _supportingOperationIds = [];
    private int _unchangedFileCount;
    private Guid? _lastTransactionId;
    private Guid[] _executedFileOperationIds = [];
    private bool _hasUndone;
    private string _activityTitle = "No activity yet";
    private string _activityMessage = "Your completed demo and undo will appear here.";
    private Guid? _readOnlyRootId;
    private bool _isFolderBusy;
    private string _folderPreviewTitle = "No folder connected";
    private string _folderPreviewMessage = "Choose a test folder to preview names, sizes, and dates.";
    private IReadOnlyList<FileItem> _demoFiles = [];
    private DeskAI.Core.Roots.AuthorizedRoot? _demoAiRoot;
    private bool _isAiBusy;
    private string _aiPreviewMessage = "AI is optional. Its ideas appear here, but it cannot change your files.";
    private string _aiDisclosureSummary = "No request has been prepared.";
    private string _aiUsageSummary = "No online AI use yet.";
    private CancellationTokenSource? _aiCancellation;

    public OrganizeViewModel(
        DemoOrganizationPlanFactory demoPlanFactory,
        TemporaryDemoPlanExecutor executor,
        IOperationJournal journal,
        IReadOnlyFolderService readOnlyFolderService,
        IAiSettingsRepository aiSettingsRepository,
        IOrganizationSuggestionProvider aiProvider)
    {
        _demoPlanFactory = demoPlanFactory ?? throw new ArgumentNullException(nameof(demoPlanFactory));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _readOnlyFolderService = readOnlyFolderService ?? throw new ArgumentNullException(nameof(readOnlyFolderService));
        _aiSettingsRepository = aiSettingsRepository ?? throw new ArgumentNullException(nameof(aiSettingsRepository));
        _aiProvider = aiProvider ?? throw new ArgumentNullException(nameof(aiProvider));
        RebuildPreviewCommand = new RelayCommand(RebuildPreview, CanEditPreview);
        SelectAllSafeCommand = new RelayCommand(SelectAllSafe, CanEditPreview);
        ClearSelectionCommand = new RelayCommand(ClearSelection, CanEditPreview);
        ExecuteDemoCommand = new AsyncRelayCommand(ExecuteDemoAsync, CanExecuteDemo);
        UndoDemoCommand = new AsyncRelayCommand(UndoDemoAsync, CanUndoDemo);
        RevokeFolderCommand = new AsyncRelayCommand(RevokeFolderAsync, CanRevokeFolder);
        GetAiSuggestionsCommand = new AsyncRelayCommand(GetAiSuggestionsAsync, () => !_isAiBusy);
        CancelAiCommand = new RelayCommand(CancelAi, () => _isAiBusy);
        RebuildPreview();
    }

    public ObservableCollection<PreviewOperationViewModel> Operations { get; } = [];
    public ObservableCollection<PreviewIssueViewModel> Issues { get; } = [];
    public ObservableCollection<ReadOnlyFileItemViewModel> FolderFiles { get; } = [];
    public ObservableCollection<AiSuggestionViewModel> AiSuggestions { get; } = [];

    public IRelayCommand RebuildPreviewCommand { get; }
    public IRelayCommand SelectAllSafeCommand { get; }
    public IRelayCommand ClearSelectionCommand { get; }
    public IAsyncRelayCommand ExecuteDemoCommand { get; }
    public IAsyncRelayCommand UndoDemoCommand { get; }
    public IAsyncRelayCommand RevokeFolderCommand { get; }
    public IAsyncRelayCommand GetAiSuggestionsCommand { get; }
    public IRelayCommand CancelAiCommand { get; }

    public string DemoRoot => _demoRoot;
    public string RevisionLabel => $"Plan revision {_revision}";
    public int SelectedOperationCount => Operations.Count(item => item.IsSelected);
    public int BlockedOperationCount => Operations.Count(item => !item.IsSelectable);
    public int ConflictCount => Issues.Count;
    public string ReadySummary => $"{SelectedOperationCount} sample file(s) ready";
    public string AttentionSummary => BlockedOperationCount == 0
        ? "Everything looks good"
        : $"{BlockedOperationCount} file(s) need a choice and will be skipped";
    public string UnchangedSummary => $"{_unchangedFileCount} other sample file(s) will stay where they are";
    public string SupportingFolderSummary => $"{_supportingOperationIds.Length} supporting folder action(s) are handled automatically.";
    public string ResultMessage => _resultMessage;
    public string ExecuteButtonText => _hasExecuted ? "Finished" : $"Organize {SelectedOperationCount} sample file(s)";
    public bool IsBusy => _isBusy;
    public string ActivityTitle => _activityTitle;
    public string ActivityMessage => _activityMessage;
    public string UndoButtonText => _hasUndone ? "Undone" : "Undo demo";
    public string FolderPreviewTitle => _folderPreviewTitle;
    public string FolderPreviewMessage => _folderPreviewMessage;
    public string FolderFileCount => FolderFiles.Count == 1 ? "1 file found" : $"{FolderFiles.Count} files found";
    public bool IsFolderBusy => _isFolderBusy;
    public bool IsAiBusy => _isAiBusy;
    public string AiPreviewMessage => _aiPreviewMessage;
    public string AiDisclosureSummary => _aiDisclosureSummary;
    public string AiUsageSummary => _aiUsageSummary;

    public async Task InitializeAsync()
    {
        try
        {
            await _executor.RecoverIncompleteAsync();
            await RefreshActivityAsync();
            var authorized = await _readOnlyFolderService.ListAuthorizedAsync();
            var latest = authorized.Count == 0 ? null : authorized[^1];
            if (latest is not null)
            {
                _readOnlyRootId = latest.Id;
                _folderPreviewTitle = latest.DisplayName;
                _folderPreviewMessage = "Connected for read-only preview. Choose it again to refresh the file list.";
                NotifyFolderStateChanged();
            }

            await RefreshAiDisclosureSummaryAsync();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.Data.Common.DbException)
        {
            _activityTitle = "History unavailable";
            _activityMessage = "DeskAI could not read activity history. No file action was started.";
            OnPropertyChanged(nameof(ActivityTitle));
            OnPropertyChanged(nameof(ActivityMessage));
        }
    }

    private async Task RefreshAiDisclosureSummaryAsync()
    {
        var settings = await _aiSettingsRepository.LoadAsync();
        _aiDisclosureSummary = settings.Mode switch
        {
            AiMode.RuleEngineOnly => "AI is off · nothing will be shared",
            AiMode.Local => $"AI on this computer · may see {FriendlyCategories(settings.CloudDisclosures)}",
            AiMode.Cloud => $"OpenRouter · may receive {FriendlyCategories(settings.CloudDisclosures)}",
            _ => "AI settings are unavailable",
        };
        OnPropertyChanged(nameof(AiDisclosureSummary));
    }

    private async Task GetAiSuggestionsAsync()
    {
        if (_isAiBusy || _demoAiRoot is null)
        {
            return;
        }

        _isAiBusy = true;
        _aiCancellation = new CancellationTokenSource();
        AiSuggestions.Clear();
        _aiPreviewMessage = "Getting AI ideas…";
        NotifyAiStateChanged();
        try
        {
            var settings = await _aiSettingsRepository.LoadAsync();
            var limits = new AiRequestLimits(
                TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 5, 120)),
                Math.Clamp(_demoFiles.Count, 1, 100),
                64 * 1024,
                128 * 1024,
                settings.MaximumEstimatedCostUsd);
            var request = AiRequestBuilder.Build(
                _demoAiRoot,
                _demoFiles,
                new HashSet<Guid>(),
                settings.CloudDisclosures,
                limits);
            var response = await _aiProvider.SuggestAsync(request, _aiCancellation.Token);
            var names = _demoFiles.ToDictionary(file => file.Id, file => Path.GetFileName(file.RelativePath));
            foreach (var suggestion in response.Suggestions)
            {
                AiSuggestions.Add(AiSuggestionViewModel.FromSuggestion(
                    suggestion, names.GetValueOrDefault(suggestion.FileId, "Unknown sample"), response.ProviderDisplayName));
            }

            _aiPreviewMessage = response.Message;
            _aiUsageSummary = response.Usage is null
                ? "No usage was reported."
                : response.Usage.EstimatedCostUsd is decimal cost
                    ? $"AI used {response.Usage.InputTokens ?? 0} input and {response.Usage.OutputTokens ?? 0} output tokens · estimated {cost:C}."
                    : $"AI used {response.Usage.InputTokens ?? 0} input and {response.Usage.OutputTokens ?? 0} output tokens. Check OpenRouter for the exact cost.";
            await RefreshAiDisclosureSummaryAsync();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.Data.Common.DbException)
        {
            _aiPreviewMessage = $"AI stayed off: {exception.Message}";
        }
        finally
        {
            _aiCancellation?.Dispose();
            _aiCancellation = null;
            _isAiBusy = false;
            NotifyAiStateChanged();
        }
    }

    private void CancelAi() => _aiCancellation?.Cancel();

    private void NotifyAiStateChanged()
    {
        OnPropertyChanged(nameof(IsAiBusy));
        OnPropertyChanged(nameof(AiPreviewMessage));
        OnPropertyChanged(nameof(AiUsageSummary));
        GetAiSuggestionsCommand.NotifyCanExecuteChanged();
        CancelAiCommand.NotifyCanExecuteChanged();
    }

    private static string FriendlyCategories(IEnumerable<DisclosureCategory> categories)
    {
        var names = categories.Select(category => category switch
        {
            DisclosureCategory.Extension => "extensions",
            DisclosureCategory.Metadata => "sizes/dates",
            DisclosureCategory.FileName => "file names",
            DisclosureCategory.FolderNames => "folder names",
            DisclosureCategory.FullPath => "full paths",
            _ => category.ToString(),
        }).ToArray();
        return names.Length == 0 ? "no file data allowed" : string.Join(", ", names);
    }

    public async Task PreviewFolderAsync(string path)
    {
        if (_isFolderBusy)
        {
            return;
        }

        _isFolderBusy = true;
        _folderPreviewTitle = "Checking this folder…";
        _folderPreviewMessage = "Reading names, sizes, and dates only.";
        FolderFiles.Clear();
        NotifyFolderStateChanged();

        try
        {
            var result = await _readOnlyFolderService.AuthorizeAndPreviewAsync(
                path,
                new MetadataScanOptions(maxDepth: 3, maxEntries: 250));
            if (!result.IsAllowed || result.Root is null)
            {
                _readOnlyRootId = null;
                _folderPreviewTitle = "Folder not connected";
                _folderPreviewMessage = result.Explanation;
                return;
            }

            _readOnlyRootId = result.Root.Id;
            _folderPreviewTitle = result.Root.DisplayName;
            _folderPreviewMessage = result.Issues.Count == 0
                ? result.Explanation
                : $"{result.Explanation} {result.Issues.Count} item(s) were skipped safely.";
            foreach (var file in result.Files.OrderBy(file => file.RelativePath))
            {
                FolderFiles.Add(ReadOnlyFileItemViewModel.FromFile(file));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Data.Common.DbException)
        {
            _readOnlyRootId = null;
            _folderPreviewTitle = "Preview unavailable";
            _folderPreviewMessage = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            _isFolderBusy = false;
            NotifyFolderStateChanged();
        }
    }

    private bool CanRevokeFolder() => _readOnlyRootId is not null && !_isFolderBusy;

    private async Task RevokeFolderAsync()
    {
        if (_readOnlyRootId is not Guid rootId || !CanRevokeFolder())
        {
            return;
        }

        _isFolderBusy = true;
        NotifyFolderStateChanged();
        try
        {
            await _readOnlyFolderService.RevokeAsync(rootId);
            _readOnlyRootId = null;
            FolderFiles.Clear();
            _folderPreviewTitle = "Folder disconnected";
            _folderPreviewMessage = "DeskAI no longer remembers permission for this folder.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Data.Common.DbException)
        {
            _folderPreviewMessage = $"DeskAI could not disconnect it: {exception.Message}";
        }
        finally
        {
            _isFolderBusy = false;
            NotifyFolderStateChanged();
        }
    }

    private void NotifyFolderStateChanged()
    {
        OnPropertyChanged(nameof(FolderPreviewTitle));
        OnPropertyChanged(nameof(FolderPreviewMessage));
        OnPropertyChanged(nameof(FolderFileCount));
        OnPropertyChanged(nameof(IsFolderBusy));
        RevokeFolderCommand.NotifyCanExecuteChanged();
    }

    private void RebuildPreview()
    {
        _revision++;
        var snapshot = _demoPlanFactory.Create(_revision);
        _plan = snapshot.Plan;
        _demoFiles = snapshot.Files;
        _demoAiRoot = snapshot.Root;
        var validations = snapshot.Validation.Operations
            .GroupBy(item => item.OperationId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Result)
                    .FirstOrDefault(result => result.Status == ValidationStatus.Blocked)
                    ?? group.First().Result);

        Operations.Clear();
        _supportingOperationIds = snapshot.Plan.Operations
            .OfType<CreateDirectoryOperation>()
            .Select(operation => operation.Id)
            .ToArray();
        foreach (var operation in snapshot.Plan.Operations.Where(operation => operation is not CreateDirectoryOperation))
        {
            var validation = validations.TryGetValue(operation.Id, out var result)
                ? result
                : ValidationResult.Blocked(ValidationReasonCode.InvalidOperation, "No safety result was produced.");
            Operations.Add(new PreviewOperationViewModel(operation, validation, NotifySelectionChanged));
        }

        Issues.Clear();
        foreach (var issue in snapshot.Plan.Issues
                     .Where(item => item.Severity is PlanIssueSeverity.Warning or PlanIssueSeverity.Conflict)
                     .OrderByDescending(item => item.Severity)
                     .ThenBy(item => item.Code))
        {
            Issues.Add(PreviewIssueViewModel.FromIssue(issue));
        }
        _unchangedFileCount = snapshot.Plan.Issues.Count(item => item.Severity == PlanIssueSeverity.Information);

        OnPropertyChanged(nameof(RevisionLabel));
        NotifySelectionChanged();
        OnPropertyChanged(nameof(ConflictCount));
        OnPropertyChanged(nameof(AttentionSummary));
        OnPropertyChanged(nameof(UnchangedSummary));
        OnPropertyChanged(nameof(SupportingFolderSummary));
        NotifyExecutionStateChanged();
    }

    private void SelectAllSafe()
    {
        foreach (var operation in Operations.Where(item => item.IsSelectable))
        {
            operation.IsSelected = true;
        }
    }

    private void ClearSelection()
    {
        foreach (var operation in Operations)
        {
            operation.IsSelected = false;
        }
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedOperationCount));
        OnPropertyChanged(nameof(BlockedOperationCount));
        OnPropertyChanged(nameof(ReadySummary));
        NotifyExecutionStateChanged();
    }

    private bool CanExecuteDemo() => !_hasExecuted && !_isBusy && SelectedOperationCount > 0;
    private bool CanEditPreview() => !_hasExecuted && !_isBusy;

    private async Task ExecuteDemoAsync()
    {
        if (_plan is null || !CanExecuteDemo())
        {
            return;
        }

        _isBusy = true;
        var selectedFileIds = Operations.Where(item => item.IsSelected).Select(item => item.Id).ToArray();
        var selectedIds = _supportingOperationIds.Concat(selectedFileIds).ToArray();
        SetOperationInteraction(false);
        _resultMessage = "Creating a controlled temporary folder and rechecking every selected action…";
        NotifyExecutionStateChanged();

        try
        {
            await _executor.PrepareAsync();
            _demoRoot = _executor.Root.CanonicalPath;
            OnPropertyChanged(nameof(DemoRoot));

            var approval = Approval.Create(Guid.NewGuid(), _plan, selectedIds, DateTimeOffset.UtcNow);
            var result = await _executor.ExecuteAsync(_plan, approval);
            var completed = result.Operations.Count(item =>
                selectedFileIds.Contains(item.OperationId) &&
                item.Outcome == DeskAI.Core.Execution.ExecutionOutcome.Completed);
            var failed = result.Operations.Count(item =>
                selectedFileIds.Contains(item.OperationId) &&
                item.Outcome == DeskAI.Core.Execution.ExecutionOutcome.Failed);
            _resultMessage = failed == 0
                ? $"Done — {completed} sample file(s) were organized. Your personal files were not used."
                : $"Organized {completed} sample file(s). {failed} stayed in place because it could not be moved safely.";
            _lastTransactionId = result.TransactionId;
            _executedFileOperationIds = selectedFileIds;
            _hasExecuted = true;
            await RefreshActivityAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Data.Common.DbException)
        {
            _resultMessage = $"The demo stopped safely: {exception.Message}";
        }
        finally
        {
            _isBusy = false;
            SetOperationInteraction(!_hasExecuted);
            NotifyExecutionStateChanged();
        }
    }

    private void NotifyExecutionStateChanged()
    {
        OnPropertyChanged(nameof(ResultMessage));
        OnPropertyChanged(nameof(ExecuteButtonText));
        OnPropertyChanged(nameof(IsBusy));
        ExecuteDemoCommand.NotifyCanExecuteChanged();
        UndoDemoCommand.NotifyCanExecuteChanged();
        RebuildPreviewCommand.NotifyCanExecuteChanged();
        SelectAllSafeCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
    }

    private bool CanUndoDemo() =>
        _lastTransactionId is not null && _hasExecuted && !_hasUndone && !_isBusy;

    private async Task UndoDemoAsync()
    {
        if (_lastTransactionId is not Guid transactionId || !CanUndoDemo())
        {
            return;
        }

        _isBusy = true;
        _resultMessage = "Checking that the sample files have not changed…";
        NotifyExecutionStateChanged();
        try
        {
            var result = await _executor.UndoAsync(transactionId);
            var restored = result.Operations.Count(item =>
                _executedFileOperationIds.Contains(item.OperationId) &&
                item.Outcome == DeskAI.Core.Execution.ExecutionOutcome.Completed);
            var failed = result.Operations.Count(item =>
                _executedFileOperationIds.Contains(item.OperationId) &&
                item.Outcome == DeskAI.Core.Execution.ExecutionOutcome.Failed);
            _resultMessage = failed == 0
                ? $"Undo complete — {restored} sample file(s) returned to their original places."
                : $"Undo restored {restored} sample file(s); {failed} stayed where they were because they changed.";
            _hasUndone = true;
            await RefreshActivityAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Data.Common.DbException)
        {
            _resultMessage = $"Undo stopped safely: {exception.Message}";
        }
        finally
        {
            _isBusy = false;
            NotifyExecutionStateChanged();
        }
    }

    private async Task RefreshActivityAsync()
    {
        var recent = await _journal.ListRecentAsync(1);
        if (recent.Count == 0)
        {
            return;
        }

        var latest = recent[0];

        var changed = latest.Operations.Count(item => item.State == JournalOperationState.Completed && item.Kind is not PlanOperationKind.CreateDirectory);
        _activityTitle = latest.Kind == ExecutionTransactionKind.Undo ? "Undo" : "Sample organization";
        _activityMessage = latest.State switch
        {
            ExecutionTransactionState.Completed => $"Completed · {changed} file(s)",
            ExecutionTransactionState.Undone => "Completed, then undone",
            ExecutionTransactionState.PartiallyCompleted => $"Partly completed · {changed} file(s)",
            ExecutionTransactionState.RecoveryRequired => "Needs review after an interrupted run",
            ExecutionTransactionState.Failed => "Stopped safely",
            _ => "Not finished",
        };
        OnPropertyChanged(nameof(ActivityTitle));
        OnPropertyChanged(nameof(ActivityMessage));
        OnPropertyChanged(nameof(UndoButtonText));
    }

    private void SetOperationInteraction(bool enabled)
    {
        foreach (var operation in Operations)
        {
            operation.SetInteractionEnabled(enabled);
        }
    }

    public void Dispose()
    {
        _aiCancellation?.Cancel();
        _aiCancellation?.Dispose();
        _aiCancellation = null;
    }
}
