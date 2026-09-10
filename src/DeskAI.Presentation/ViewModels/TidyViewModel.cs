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
        Destination = "Goes to " + suggestion.DestinationRelativePath.Replace("\\", " › ", StringComparison.Ordinal);
        _keepBoth = suggestion.Choice == SameNameChoice.KeepBoth;
        _isIncluded = MoveOperationId is not null;
        _selectionChanged = selectionChanged;
        _keepBothChanged = keepBothChanged;
    }

    public Guid FileId { get; }
    public string FileName { get; }
    public string Reason { get; }
    public string Destination { get; }
    public Guid? MoveOperationId { get; }
    public bool HasSameName { get; }

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
    public TidyGroupViewModel(string folder, IEnumerable<TidyItemViewModel> items)
    {
        Folder = folder;
        DisplayName = folder.Replace("\\", " › ", StringComparison.Ordinal);
        Items = new ObservableCollection<TidyItemViewModel>(items);
    }

    public string Folder { get; }
    public string DisplayName { get; }
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
/// In this version the page stops at the list. The Tidy button is shown switched off with a
/// note saying so, because a button that looked ready and did nothing would be worse than an
/// honest "not yet". Nothing on this page can move a file.
/// </remarks>
public sealed class TidyViewModel : ObservableObject
{
    private readonly ConnectedFolderService _folders;
    private readonly TidyPermissionService _permission;
    private readonly TidySuggestionService _suggestions;
    private readonly Dictionary<Guid, SameNameChoice> _choices = [];
    private TidyFolderOption? _selectedFolder;
    private Guid _planId = Guid.NewGuid();
    private int _revision;
    private bool _isBusy;
    private bool _needsPermission;
    private string _message = string.Empty;
    private string _summaryTitle = string.Empty;
    private string _limitNote = string.Empty;
    private Task _pending = Task.CompletedTask;

    public TidyViewModel(
        ConnectedFolderService folders,
        TidyPermissionService permission,
        TidySuggestionService suggestions)
    {
        _folders = folders;
        _permission = permission;
        _suggestions = suggestions;
        StopTidyingCommand = new AsyncRelayCommand(StopTidyingAsync, () => SelectedFolder?.CanTidy == true && !IsBusy);
        RefreshCommand = new AsyncRelayCommand(() => _pending = LoadAsync(), () => SelectedFolder is not null && !IsBusy);
    }

    public ObservableCollection<TidyFolderOption> Folders { get; } = [];
    public ObservableCollection<TidyGroupViewModel> Groups { get; } = [];
    public ObservableCollection<TidyLeftAloneViewModel> LeftAlone { get; } = [];

    public AsyncRelayCommand StopTidyingCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }

    public TidyFolderOption? SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (SetProperty(ref _selectedFolder, value))
            {
                _choices.Clear();
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

    /// <summary>Always false in this version: tidying itself arrives in the next update.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Bound by the page like every other property; it becomes state when tidying ships.")]
    public bool CanPressTidy => false;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Bound by the page like every other property; it changes when tidying ships.")]
    public string TidyNote => "Tidying arrives in the next update. Nothing moves yet — this is what it would do.";

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

    private async Task LoadAsync()
    {
        Groups.Clear();
        LeftAlone.Clear();
        LimitNote = string.Empty;
        SummaryTitle = string.Empty;
        NeedsPermission = SelectedFolder is { CanTidy: false };
        RaiseListChanges();
        if (SelectedFolder is not { CanTidy: true } folder)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var preview = await _suggestions.PreviewAsync(folder.Id, _planId, ++_revision, _choices).ConfigureAwait(true);
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
        foreach (var group in preview.Suggestions
                     .GroupBy(item => item.DestinationFolder, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            Groups.Add(new TidyGroupViewModel(
                group.Key,
                group.Select(item => new TidyItemViewModel(item, OnSelectionChanged, OnKeepBothChanged))));
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
        OnSelectionChanged();
    }

    private static bool IsExpectedFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.Data.Common.DbException;
}
