using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;
using DeskAI.Core.Search;

namespace DeskAI.App.ViewModels;

/// <summary>One matched file, formatted for display.</summary>
/// <remarks>
/// Built from a <see cref="SearchHit"/>, which carries a folder name and a path relative to
/// it. No absolute path is ever formed here, so a result cannot reveal where a folder sits
/// on disk.
/// </remarks>
public sealed record SearchResultViewModel(string Name, string Location, string Size, string Changed)
{
    public static SearchResultViewModel From(SearchHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        var file = hit.File;
        var folder = Path.GetDirectoryName(file.RelativePath);
        var location = string.IsNullOrEmpty(folder)
            ? hit.RootName
            : $"{hit.RootName} / {folder}";

        return new SearchResultViewModel(
            file.Name,
            location,
            DescribeSize(file.SizeBytes),
            file.ModifiedAtUtc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture));
    }

    private static string DescribeSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.#} GB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):0.#} MB",
        >= 1024 => $"{bytes / 1024d:0.#} KB",
        _ => $"{bytes} bytes",
    };
}

/// <summary>One file whose words matched, with the piece of text that matched.</summary>
/// <remarks>
/// The snippet is shown so a person can see why a file matched rather than trusting that it
/// did. It is text from a file, which makes it untrusted input: it is displayed and nothing
/// more, exactly like a file name.
/// </remarks>
public sealed record ContentHitViewModel(string Name, string Location, string Snippet);

/// <summary>Explains one attempted local read without keeping the file's text.</summary>
public sealed record ContentCheckViewModel(string Name, string Location, string Result)
{
    public static ContentCheckViewModel From(ContentFileCheck check)
    {
        var folder = Path.GetDirectoryName(check.RelativePath);
        var location = string.IsNullOrEmpty(folder)
            ? check.RootName
            : $"{check.RootName} / {folder}";
        var isPdf = string.Equals(Path.GetExtension(check.Name), ".pdf", StringComparison.OrdinalIgnoreCase);
        var result = check.Status switch
        {
            ContentCheckStatus.Matched when check.WasTruncated && isPdf =>
                "Matched in the part read (up to the first 20 pages or 64 KB of text); there may be more.",
            ContentCheckStatus.Matched when check.WasTruncated => "Matched in the part read; there may be more.",
            ContentCheckStatus.Matched => "Matched the words inside.",
            ContentCheckStatus.NoMatch when check.WasTruncated && isPdf =>
                "No match in the part read (up to the first 20 pages or 64 KB of text); the words may be later.",
            ContentCheckStatus.NoMatch when check.WasTruncated => "No match in the part read; there may be more.",
            ContentCheckStatus.NoMatch => "Read, but the words did not match.",
            _ => $"Could not read: {check.Explanation}",
        };
        return new ContentCheckViewModel(check.Name, location, result);
    }
}

/// <summary>One saved search, formatted for its row.</summary>
public sealed record SavedSearchViewModel(Guid Id, string Name, string Phrase);

/// <summary>A folder the person can deliberately limit this search to.</summary>
public sealed record SearchFolderChoiceViewModel(Guid Id, string Name)
{
    public static SearchFolderChoiceViewModel All { get; } = new(Guid.Empty, "All connected folders");
}

/// <summary>One connected folder, formatted for the folder list.</summary>
/// <remarks>
/// The row states whether DeskAI may read inside this folder's files. A permission granted
/// but not shown is one a person cannot reconsider, so it is written on the row rather than
/// left implicit in a button label.
/// </remarks>
public sealed record ConnectedFolderViewModel(
    Guid Id,
    string Name,
    string Path,
    string Remembered,
    bool CanReadContent,
    bool CanReadDocuments = false,
    bool CanReadPdf = false,
    bool CanReadSlides = false)
{
    public string ContentState => CanReadContent
        ? CanReadPdf
            ? CanReadSlides
                ? "DeskAI can read notes, Word, Excel, PDF text, and PowerPoint slide text here."
                : "DeskAI can read notes, Word, Excel, and PDF text here."
            : CanReadSlides
            ? "DeskAI can read notes, Word, Excel, and PowerPoint slide text here. PDFs need your permission."
            : CanReadDocuments
            ? "DeskAI can read notes, Word, and Excel here. PDFs and PowerPoint need your permission."
            : "DeskAI can read plain-text files here. Word and Excel still need your permission."
        : "Names, sizes, and dates only.";

    public bool CanUpgradeDocuments => CanReadContent && !CanReadDocuments;
    public bool CanUpgradePdf => CanReadDocuments && !CanReadPdf;
    public bool CanUpgradeSlides => CanReadDocuments && !CanReadSlides;

    public string ContentAction => CanReadContent ? "Stop reading inside" : "Read inside files";

    /// <summary>The inverse of <see cref="CanReadContent"/>, so the row can show the plain pill without a converter.</summary>
    public bool IsNamesOnly => !CanReadContent;

    public static ConnectedFolderViewModel From(ConnectedFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var remembered = folder.FileCount switch
        {
            0 => "Nothing remembered yet — refresh to scan it.",
            1 => "1 file remembered",
            _ => $"{folder.FileCount} files remembered",
        };

        return new ConnectedFolderViewModel(
            folder.Id,
            folder.Name,
            folder.Path,
            remembered,
            folder.CanReadContent,
            folder.CanReadDocuments,
            folder.CanReadPdf,
            folder.CanReadSlides);
    }
}

/// <summary>
/// Drives the Search page: reads a typed phrase, shows how it was understood, and lists
/// what matched.
/// </summary>
/// <remarks>
/// <para>
/// All search rules live in <see cref="FileSearchService"/>. This class only turns an
/// outcome into text and lists, which keeps the decision about which folders may be read
/// out of the App layer entirely.
/// </para>
/// <para>
/// The three states a person can end up in are kept distinct on purpose, because collapsing
/// them is how a search screen starts lying: not understood, understood but nothing matched,
/// and matched. Only the last one shows results.
/// </para>
/// </remarks>
public sealed class SearchViewModel : ObservableObject
{
    private readonly FileSearchService _search;
    private readonly ConnectedFolderService _folders;
    private readonly ContentSearchService _insideFiles;
    private readonly ISavedSearchRepository _savedSearches;
    private readonly IClock _clock;
    private readonly SearchRequest _request;
    private readonly SentenceAiService _sentenceAi;
    private readonly VisualSearchService? _visualSearch;
    private SentenceAiStatus? _aiStatus;
    private string _aiMessage = string.Empty;
    private string _phrase = string.Empty;
    private string _folderMessage = "No folders connected yet.";
    private bool _isFolderBusy;
    private string _statusTitle = "Search your connected folders";
    private string _statusMessage =
        "Try \"photos from last month\" or \"documents over 10 mb\". DeskAI reads only the folders you connected.";
    private string _scopeMessage = string.Empty;
    private string _insideMessage = string.Empty;
    private string _visualMessage = string.Empty;
    private bool _showsInsideFiles;
    private bool _isBusy;
    private bool _hasSearched;
    private SearchFolderChoiceViewModel _selectedFolder = SearchFolderChoiceViewModel.All;

    public SearchViewModel(
        FileSearchService search,
        ConnectedFolderService folders,
        ContentSearchService insideFiles,
        ISavedSearchRepository savedSearches,
        IClock clock,
        SearchRequest request,
        SentenceAiService sentenceAi,
        VisualSearchService? visualSearch = null)
    {
        _search = search;
        _folders = folders;
        _insideFiles = insideFiles;
        _savedSearches = savedSearches;
        _clock = clock;
        _request = request;
        _sentenceAi = sentenceAi;
        _visualSearch = visualSearch;
        SearchCommand = new AsyncRelayCommand(RunAsync, () => !IsBusy);
        RunSavedSearchCommand = new AsyncRelayCommand<Guid>(RunSavedSearchAsync, _ => !IsBusy);
        DeleteSavedSearchCommand = new AsyncRelayCommand<Guid>(DeleteSavedSearchAsync, _ => !IsBusy);
        RefreshFolderCommand = new AsyncRelayCommand<Guid>(RefreshFolderAsync, _ => !IsFolderBusy);
        DisconnectFolderCommand = new AsyncRelayCommand<Guid>(DisconnectFolderAsync, _ => !IsFolderBusy);
    }

    public ObservableCollection<ConnectedFolderViewModel> Folders { get; } = [];

    public ObservableCollection<SearchFolderChoiceViewModel> SearchFolders { get; } =
        [SearchFolderChoiceViewModel.All];

    public SearchFolderChoiceViewModel SelectedFolder
    {
        get => _selectedFolder;
        set => SetProperty(ref _selectedFolder, value ?? SearchFolderChoiceViewModel.All);
    }

    /// <summary>Files whose words matched, from folders that allowed reading inside.</summary>
    public ObservableCollection<ContentHitViewModel> InsideResults { get; } = [];

    public ObservableCollection<ContentCheckViewModel> CheckedFiles { get; } = [];

    public ObservableCollection<ContentHitViewModel> VisualResults { get; } = [];

    public string VisualMessage
    {
        get => _visualMessage;
        private set => SetProperty(ref _visualMessage, value);
    }

    public bool ShowsVisualResults => VisualResults.Count > 0 || !string.IsNullOrEmpty(VisualMessage);

    public bool CanSearchPictures => HasAi && !string.IsNullOrWhiteSpace(Phrase) && !IsBusy;

    public string VisualAiName => _aiStatus?.ServiceName ?? "AI";

    public bool HasCheckedFiles => CheckedFiles.Count > 0;

    /// <summary>What was looked at inside files, stated rather than implied.</summary>
    public string InsideMessage
    {
        get => _insideMessage;
        private set => SetProperty(ref _insideMessage, value);
    }

    public bool ShowsInsideFiles
    {
        get => _showsInsideFiles;
        private set => SetProperty(ref _showsInsideFiles, value);
    }

    public AsyncRelayCommand<Guid> RefreshFolderCommand { get; }

    public AsyncRelayCommand<Guid> DisconnectFolderCommand { get; }

    public string FolderMessage
    {
        get => _folderMessage;
        private set => SetProperty(ref _folderMessage, value);
    }

    public bool IsFolderBusy
    {
        get => _isFolderBusy;
        private set
        {
            if (SetProperty(ref _isFolderBusy, value))
            {
                OnPropertyChanged(nameof(IsFolderIdle));
                RefreshFolderCommand.NotifyCanExecuteChanged();
                DisconnectFolderCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>The inverse of <see cref="IsFolderBusy"/>, so the view needs no converter.</summary>
    public bool IsFolderIdle => !IsFolderBusy;

    public bool HasFolders => Folders.Count > 0;

    public ObservableCollection<string> Chips { get; } = [];

    public ObservableCollection<SearchResultViewModel> Results { get; } = [];

    public AsyncRelayCommand SearchCommand { get; }

    /// <summary>Bounded in the view as well, so an over-long phrase cannot be submitted.</summary>
    public static int MaxPhraseLength => NaturalLanguageQueryTranslator.MaxInputLength;

    public string Phrase
    {
        get => _phrase;
        set
        {
            if (SetProperty(ref _phrase, value))
            {
                OnPropertyChanged(nameof(CanSaveCurrentSearch));
                OnPropertyChanged(nameof(CanAskAi));
                OnPropertyChanged(nameof(CanSearchPictures));
            }
        }
    }

    /// <summary>Whether AI is set up at all; the "Let AI read this" button exists only then.</summary>
    public bool HasAi => _aiStatus?.IsSetUp == true;

    public bool CanAskAi => HasAi && !string.IsNullOrWhiteSpace(Phrase) && !IsBusy;

    public string AskAiText => HasAi ? $"Let {_aiStatus!.ServiceName} read this" : "Let AI read this";

    /// <summary>What AI made of the phrase, or why it could not be asked. Empty when nothing was asked.</summary>
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

    /// <summary>
    /// Says who would get the phrase and where. Sends nothing; the page shows this in a dialog
    /// and calls <see cref="AskAiToReadAsync"/> only if Send is pressed.
    /// </summary>
    public async Task<SentenceAiQuestion?> PrepareAiReadingAsync()
    {
        var prepared = await _sentenceAi.PrepareAsync(SentenceTask.SearchPhrase, Phrase).ConfigureAwait(true);
        if (prepared.Question is null)
        {
            AiMessage = prepared.Explanation;
        }

        return prepared.Question;
    }

    /// <summary>Sends the prepared phrase; on success the AI's reading replaces the phrase and is searched.</summary>
    public async Task AskAiToReadAsync(SentenceAiQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);
        IsBusy = true;
        SentenceAiAnswer answer;
        try
        {
            answer = await _sentenceAi.AskAsync(question).ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFolderFailure(exception))
        {
            AiMessage = $"DeskAI stopped safely: {exception.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        AiMessage = answer.Message;
        if (answer.Succeeded && answer.Reading is { } reading)
        {
            Phrase = reading;
            await RunAsync().ConfigureAwait(true);
        }
    }

    public string StatusTitle
    {
        get => _statusTitle;
        private set => SetProperty(ref _statusTitle, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>States the real scope, so the page never implies the whole computer.</summary>
    public string ScopeMessage
    {
        get => _scopeMessage;
        private set
        {
            if (SetProperty(ref _scopeMessage, value))
            {
                OnPropertyChanged(nameof(HasScopeMessage));
            }
        }
    }

    public bool HasScopeMessage => !string.IsNullOrEmpty(ScopeMessage);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                SearchCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(CanAskAi));
                OnPropertyChanged(nameof(CanSearchPictures));
            }
        }
    }

    public bool HasResults => Results.Count > 0;

    public bool HasChips => Chips.Count > 0;

    /// <summary>True only after a search that found nothing, so the first visit stays calm.</summary>
    public bool ShowsNothingFound => _hasSearched && Results.Count == 0 && InsideResults.Count == 0
        && VisualResults.Count == 0;

    public ObservableCollection<SavedSearchViewModel> SavedSearches { get; } = [];

    public AsyncRelayCommand<Guid> RunSavedSearchCommand { get; }

    public AsyncRelayCommand<Guid> DeleteSavedSearchCommand { get; }

    public bool HasSavedSearches => SavedSearches.Count > 0;

    /// <summary>A phrase must exist before there is anything worth saving.</summary>
    public bool CanSaveCurrentSearch => !string.IsNullOrWhiteSpace(Phrase);

    public static int MaxSavedSearchNameLength => SavedSearch.MaxNameLength;

    /// <summary>
    /// Loads the folder list and saved searches when the page opens, then runs the saved search
    /// My workspace asked for, or the phrase typed in the top bar, if any.
    /// </summary>
    /// <remarks>
    /// The request is taken once. Running it is exactly what pressing Run on that saved search,
    /// or typing the phrase here and pressing Search, does, so arriving from elsewhere can show
    /// nothing a person could not have asked for on this page.
    /// </remarks>
    public async Task InitializeAsync()
    {
        await ReloadFoldersAsync().ConfigureAwait(true);
        await ReloadSavedSearchesAsync().ConfigureAwait(true);
        try
        {
            _aiStatus = await _sentenceAi.GetStatusAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFolderFailure(exception))
        {
            // No AI button rather than a broken one; searching itself is unaffected.
            _aiStatus = null;
        }

        OnPropertyChanged(nameof(HasAi));
        OnPropertyChanged(nameof(CanAskAi));
        OnPropertyChanged(nameof(AskAiText));
        OnPropertyChanged(nameof(CanSearchPictures));
        OnPropertyChanged(nameof(VisualAiName));

        if (_request.TakePhrase() is { } typed)
        {
            Phrase = typed.Length > MaxPhraseLength ? typed[..MaxPhraseLength] : typed;
            await RunAsync().ConfigureAwait(true);
            return;
        }

        if (_request.Take() is not { } requestedId)
        {
            return;
        }

        var requested = SavedSearches.FirstOrDefault(item => item.Id == requestedId);
        if (requested is null)
        {
            StatusTitle = "That saved search no longer exists";
            StatusMessage = "It may have been removed. Your other saved searches are below.";
            return;
        }

        Phrase = requested.Phrase;
        await RunAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Saves the phrase currently in the box under <paramref name="name"/>.
    /// </summary>
    /// <remarks>
    /// The phrase is stored, not the query it produced, so a relative phrase stays relative.
    /// No folder is recorded: a saved search is re-scoped against the connected folders each
    /// time it runs, and can never act as a lingering grant to somewhere since disconnected.
    /// </remarks>
    public async Task SaveCurrentSearchAsync(string name)
    {
        if (!CanSaveCurrentSearch)
        {
            return;
        }

        try
        {
            var saved = SavedSearch.Create(Guid.NewGuid(), name, Phrase, _clock.UtcNow);
            if (SavedSearches.Count >= SavedSearch.MaxSavedSearches)
            {
                StatusTitle = "That is as many as DeskAI keeps";
                StatusMessage =
                    $"You already have {SavedSearch.MaxSavedSearches} saved searches. Remove one to save another.";
                return;
            }

            await _savedSearches.SaveAsync(saved).ConfigureAwait(true);
            await ReloadSavedSearchesAsync().ConfigureAwait(true);
            StatusTitle = $"Saved as \"{saved.Name}\"";
            StatusMessage = "Running it searches again from scratch. It never moves or changes a file.";
        }
        catch (ArgumentException exception)
        {
            StatusTitle = "That name will not work";
            StatusMessage = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            // A duplicate name, surfaced by the repository as a readable message.
            StatusTitle = "That name is taken";
            StatusMessage = exception.Message;
        }
    }

    private async Task RunSavedSearchAsync(Guid savedSearchId)
    {
        var match = SavedSearches.FirstOrDefault(item => item.Id == savedSearchId);
        if (match is null)
        {
            return;
        }

        Phrase = match.Phrase;
        await RunAsync().ConfigureAwait(true);
    }

    private async Task DeleteSavedSearchAsync(Guid savedSearchId)
    {
        await _savedSearches.RemoveAsync(savedSearchId).ConfigureAwait(true);
        await ReloadSavedSearchesAsync().ConfigureAwait(true);
    }

    private async Task ReloadSavedSearchesAsync()
    {
        var stored = await _savedSearches.ListAsync().ConfigureAwait(true);
        SavedSearches.Clear();
        foreach (var item in stored)
        {
            SavedSearches.Add(new SavedSearchViewModel(item.Id, item.Name, item.Phrase));
        }

        OnPropertyChanged(nameof(HasSavedSearches));
    }

    /// <summary>
    /// Connects a folder the person picked and confirmed in the view.
    /// </summary>
    /// <remarks>
    /// The path arrives already chosen through the Windows picker and an explicit
    /// confirmation, so this method never invents or guesses a location.
    /// </remarks>
    public async Task ConnectFolderAsync(string path)
    {
        IsFolderBusy = true;
        FolderMessage = "Checking this folder…";
        try
        {
            var result = await _folders.ConnectAsync(path).ConfigureAwait(true);
            await ReloadFoldersAsync().ConfigureAwait(true);
            FolderMessage = result.IsAllowed
                ? $"{result.Folder?.Name}: {result.Explanation}"
                : result.Explanation;
        }
        catch (Exception exception) when (IsExpectedFolderFailure(exception))
        {
            FolderMessage = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsFolderBusy = false;
        }
    }

    private async Task RefreshFolderAsync(Guid rootId)
    {
        IsFolderBusy = true;
        try
        {
            var result = await _folders.RefreshAsync(rootId).ConfigureAwait(true);
            await ReloadFoldersAsync().ConfigureAwait(true);
            FolderMessage = result.Explanation;
        }
        catch (Exception exception) when (IsExpectedFolderFailure(exception))
        {
            FolderMessage = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsFolderBusy = false;
        }
    }

    /// <summary>
    /// Says why choosing a folder did not work.
    /// </summary>
    /// <remarks>
    /// The page owns the folder dialog, so it is the only thing that knows a pick failed.
    /// Reporting it here keeps the message in the same place as every other folder message,
    /// instead of a dialog that appears and vanishes.
    /// </remarks>
    public void ReportFolderProblem(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        FolderMessage = message;
    }

    /// <summary>
    /// Grants or withdraws permission to read inside a folder's text files.
    /// </summary>
    /// <remarks>
    /// Granting is only ever called after the page has shown a confirmation naming exactly
    /// what will be read. Withdrawing needs no confirmation: taking a permission back is
    /// never the dangerous direction.
    /// </remarks>
    public async Task SetContentPermissionAsync(Guid rootId, bool allow)
    {
        IsFolderBusy = true;
        try
        {
            var result = allow
                ? await _folders.AllowDocumentsAsync(rootId).ConfigureAwait(true)
                : await _folders.StopContentAsync(rootId).ConfigureAwait(true);
            await ReloadFoldersAsync().ConfigureAwait(true);
            FolderMessage = result.Explanation;
        }
        catch (Exception exception) when (IsExpectedFolderFailure(exception))
        {
            FolderMessage = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsFolderBusy = false;
        }
    }

    /// <summary>The page asks separately before granting PDF reading.</summary>
    public async Task SetPdfPermissionAsync(Guid rootId, bool allow)
    {
        IsFolderBusy = true;
        try
        {
            var result = allow
                ? await _folders.AllowPdfAsync(rootId).ConfigureAwait(true)
                : await _folders.StopPdfAsync(rootId).ConfigureAwait(true);
            await ReloadFoldersAsync().ConfigureAwait(true);
            FolderMessage = result.Explanation;
        }
        catch (Exception exception) when (IsExpectedFolderFailure(exception))
        {
            FolderMessage = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsFolderBusy = false;
        }
    }

    /// <summary>The page asks separately before granting slide-text reading.</summary>
    public async Task SetSlidesPermissionAsync(Guid rootId, bool allow)
    {
        IsFolderBusy = true;
        try
        {
            var result = allow
                ? await _folders.AllowSlidesAsync(rootId).ConfigureAwait(true)
                : await _folders.StopSlidesAsync(rootId).ConfigureAwait(true);
            await ReloadFoldersAsync().ConfigureAwait(true);
            FolderMessage = result.Explanation;
        }
        catch (Exception exception) when (IsExpectedFolderFailure(exception))
        {
            FolderMessage = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsFolderBusy = false;
        }
    }

    private async Task DisconnectFolderAsync(Guid rootId)
    {
        IsFolderBusy = true;
        try
        {
            await _folders.DisconnectAsync(rootId).ConfigureAwait(true);
            await ReloadFoldersAsync().ConfigureAwait(true);

            // Results already on screen may have come from the folder just removed, so they
            // are cleared rather than left behind as stale rows.
            Reset();
            RaiseListChanges();
            FolderMessage = "Disconnected. Everything remembered about it has been forgotten.";
        }
        catch (Exception exception) when (IsExpectedFolderFailure(exception))
        {
            FolderMessage = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsFolderBusy = false;
        }
    }

    private async Task ReloadFoldersAsync()
    {
        var connected = await _folders.ListAsync().ConfigureAwait(true);
        Folders.Clear();
        var selectedId = SelectedFolder.Id;
        SearchFolders.Clear();
        SearchFolders.Add(SearchFolderChoiceViewModel.All);
        foreach (var folder in connected)
        {
            Folders.Add(ConnectedFolderViewModel.From(folder));
            SearchFolders.Add(new SearchFolderChoiceViewModel(folder.Id, folder.Name));
        }

        SelectedFolder = SearchFolders.FirstOrDefault(choice => choice.Id == selectedId)
            ?? SearchFolderChoiceViewModel.All;
        OnPropertyChanged(nameof(SelectedFolder));

        OnPropertyChanged(nameof(HasFolders));
        if (Folders.Count == 0)
        {
            FolderMessage = "No folders connected yet.";
        }
        else if (FolderMessage == "No folders connected yet.")
        {
            FolderMessage = Folders.Count == 1
                ? "1 folder connected."
                : $"{Folders.Count} folders connected.";
        }
    }

    private static bool IsExpectedFolderFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or System.Data.Common.DbException;

    private async Task RunAsync()
    {
        IsBusy = true;
        try
        {
            var selectedRootId = SelectedFolder.Id == Guid.Empty ? (Guid?)null : SelectedFolder.Id;
            var outcome = await _search.SearchAsync(Phrase, _clock.UtcNow, selectedRootId).ConfigureAwait(true);
            Apply(outcome);

            // Looking inside files is a separate pass, after the results are on screen, and
            // only in folders that allowed it. It finds files whose words match even when
            // the name says nothing, which is the whole reason someone grants the permission.
            ApplyInsideFiles(await _insideFiles.SearchAsync(Phrase, _clock.UtcNow, selectedRootId).ConfigureAwait(true));
        }
        catch (ArgumentException)
        {
            // The only rejection the translator raises is an over-long phrase, which the
            // view already prevents. Say what to do rather than showing an error code.
            Reset();
            _hasSearched = false;
            StatusTitle = "That is a bit long";
            StatusMessage = $"Try a shorter phrase, under {MaxPhraseLength} characters.";
            ScopeMessage = string.Empty;
            RaiseListChanges();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Called only after the page asks to read pictures for this search.</summary>
    public async Task<VisualSearchBatch?> PreparePictureSearchAsync(bool visualReadApproved)
    {
        if (_visualSearch is null)
        {
            VisualMessage = "Picture search is not available in this build.";
            OnPropertyChanged(nameof(ShowsVisualResults));
            return null;
        }

        IsBusy = true;
        VisualResults.Clear();
        VisualMessage = "Finding pictures in the selected connected folder…";
        OnPropertyChanged(nameof(ShowsVisualResults));
        try
        {
            var selectedRootId = SelectedFolder.Id == Guid.Empty ? (Guid?)null : SelectedFolder.Id;
            var (batch, message) = await _visualSearch.PrepareAsync(Phrase, selectedRootId,
                visualReadApproved).ConfigureAwait(true);
            VisualMessage = message;
            return batch;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Cloud approval comes from the separate Send dialog; local uses the read dialog.</summary>
    public async Task SearchPicturesAsync(VisualSearchBatch batch, bool cloudSendApproved)
    {
        if (_visualSearch is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var outcome = await _visualSearch.SearchAsync(batch, cloudSendApproved).ConfigureAwait(true);
            VisualResults.Clear();
            foreach (var hit in outcome.Matches)
            {
                VisualResults.Add(new ContentHitViewModel(hit.Name, hit.Location,
                    $"AI saw: {hit.Explanation}"));
            }

            VisualMessage = outcome.Message;
            OnPropertyChanged(nameof(ShowsVisualResults));
            if (outcome.Matches.Count > 0 && Results.Count == 0 && InsideResults.Count == 0)
            {
                StatusTitle = outcome.Matches.Count == 1 ? "1 file found" : $"{outcome.Matches.Count} pictures matched";
                StatusMessage = "These pictures may match your description. Check the file yourself; AI can be mistaken.";
                OnPropertyChanged(nameof(ShowsNothingFound));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(SearchOutcome outcome)
    {
        Reset();
        _hasSearched = true;

        foreach (var chip in outcome.Translation.Chips)
        {
            Chips.Add(chip.Label);
        }

        if (outcome.FoldersSearched == 0)
        {
            StatusTitle = "No folders connected yet";
            // The Connect button is on this page. Sending people to Organize pointed them at
            // a read-only preview that never fills search.
            StatusMessage =
                "Press \"Connect a folder\" below first. DeskAI can only search folders you have chosen.";
            ScopeMessage = string.Empty;
        }
        else if (outcome.UnderstoodNothing)
        {
            // Refusing here is deliberate: an unfiltered query would list every remembered
            // file, which would look like a successful search for a phrase nobody understood.
            StatusTitle = "I did not understand that";
            StatusMessage =
                "Try naming a type, a size, or a time — for example \"photos\", \"over 10 mb\", or \"last month\".";
            ScopeMessage = string.Empty;
        }
        else
        {
            foreach (var hit in outcome.Hits)
            {
                Results.Add(SearchResultViewModel.From(hit));
            }

            StatusTitle = Results.Count switch
            {
                0 => "Nothing matched",
                1 => "1 file found",
                _ => $"{Results.Count} files found",
            };

            StatusMessage = outcome.ReachedLimit
                ? "Showing the first matches only. Narrow the search to see fewer, more useful results."
                : Results.Count == 0 && HasAi
                    ? "Names did not match. To search what a picture shows, press Find pictures with AI."
                    : "These matches came from the file names and details DeskAI remembered. Nothing was changed.";

            ScopeMessage = outcome.FoldersSearched == 1
                ? "Searched 1 connected folder."
                : $"Searched {outcome.FoldersSearched} connected folders.";
        }

        RaiseListChanges();
    }

    /// <summary>
    /// Shows what was found inside files, and how much was actually looked at.
    /// </summary>
    /// <remarks>
    /// The count of files read is stated rather than hidden. "Nothing matched" and "nothing
    /// matched in the first fifty files DeskAI checked" mean different things to someone
    /// deciding whether to trust the answer.
    /// </remarks>
    private void ApplyInsideFiles(ContentSearchOutcome outcome)
    {
        InsideResults.Clear();
        CheckedFiles.Clear();
        ShowsInsideFiles = outcome.WasSearched;

        if (!outcome.WasSearched)
        {
            InsideMessage = string.Empty;
            OnPropertyChanged(nameof(ShowsInsideFiles));
            OnPropertyChanged(nameof(HasCheckedFiles));
            return;
        }

        foreach (var check in outcome.CheckedFiles)
        {
            CheckedFiles.Add(ContentCheckViewModel.From(check));
        }

        foreach (var hit in outcome.Hits)
        {
            var folder = Path.GetDirectoryName(hit.RelativePath);
            InsideResults.Add(new ContentHitViewModel(
                hit.Name,
                string.Join(" / ", new[] { hit.RootName, folder, hit.Section }
                    .Where(part => !string.IsNullOrEmpty(part))),
                hit.Snippet));
        }

        var read = outcome.FilesRead == 1 ? "1 file" : $"{outcome.FilesRead} files";
        InsideMessage = outcome.Hits.Count switch
        {
            0 when outcome.FilesRead == 0 =>
                "No eligible files were opened. If you added a file, press Refresh on its connected folder. PDF text also needs its own PDF reading permission. Search 'pdf' alone to see remembered PDF names.",
            0 when outcome.ReachedLimit =>
                $"Nothing found in the first {read} DeskAI checked. Narrow the search to look at different files.",
            0 => $"Nothing found inside the {read} DeskAI checked.",
            _ when outcome.ReachedLimit =>
                $"Found in {outcome.Hits.Count} of the first {read} DeskAI checked. There may be more.",
            _ => $"Found in {outcome.Hits.Count} of {read} DeskAI checked.",
        };
        if (outcome.FilesTruncated > 0)
        {
            InsideMessage += outcome.FilesTruncated == 1
                ? " One file was only partly read."
                : $" {outcome.FilesTruncated} files were only partly read.";
        }

        if (outcome.FilesSkipped > 0)
        {
            InsideMessage += outcome.FilesSkipped == 1
                ? " One file could not be read."
                : $" {outcome.FilesSkipped} files could not be read.";
        }

        if (outcome.CheckedFiles.Count > 0)
        {
            InsideMessage += " See Files checked for each result. Search 'pdf' alone to list remembered PDFs by name.";
        }

        if (outcome.Hits.Count > 0 && Results.Count == 0)
        {
            StatusTitle = outcome.Hits.Count == 1 ? "1 file found" : $"{outcome.Hits.Count} files found";
            StatusMessage = "These files matched words inside them. DeskAI did not change anything.";
        }

        OnPropertyChanged(nameof(ShowsInsideFiles));
        OnPropertyChanged(nameof(ShowsNothingFound));
        OnPropertyChanged(nameof(HasCheckedFiles));
    }

    private void Reset()
    {
        Chips.Clear();
        Results.Clear();
        InsideResults.Clear();
        CheckedFiles.Clear();
        VisualResults.Clear();
        VisualMessage = string.Empty;
        OnPropertyChanged(nameof(ShowsVisualResults));
        ShowsInsideFiles = false;
        InsideMessage = string.Empty;
        OnPropertyChanged(nameof(HasCheckedFiles));
    }

    private void RaiseListChanges()
    {
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasChips));
        OnPropertyChanged(nameof(ShowsNothingFound));
        OnPropertyChanged(nameof(ShowsInsideFiles));
    }
}
