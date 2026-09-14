using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Search;
using DeskAI.Core.Workspace;

namespace DeskAI.App.ViewModels;

/// <summary>One pinned search as a tile: its name and what its count may truthfully say.</summary>
public sealed record PinnedSearchTileViewModel(Guid Id, string Name, string Count);

/// <summary>A saved search that is not pinned, offered with a Pin button.</summary>
public sealed record UnpinnedSearchViewModel(Guid Id, string Name, string Phrase);

/// <summary>
/// One starter pack's card. The result of adding it is shown on the card itself, because the
/// answer to pressing a button belongs next to that button.
/// </summary>
public sealed class StarterPackCardViewModel(string id, string name, string summary) : ObservableObject
{
    private string _result = string.Empty;

    public string Id { get; } = id;

    public string Name { get; } = name;

    public string Summary { get; } = summary;

    public string Result
    {
        get => _result;
        set
        {
            if (SetProperty(ref _result, value))
            {
                OnPropertyChanged(nameof(HasResult));
            }
        }
    }

    public bool HasResult => !string.IsNullOrEmpty(Result);
}

/// <summary>
/// The logic behind My workspace: starter packs and pinned searches.
/// </summary>
/// <remarks>
/// <para>
/// Nothing on this page changes a file. Adding a pack creates saved searches and switched-off
/// rules; pinning changes a flag on a saved search; a tile's count is an ordinary search over
/// what DeskAI remembers. The page holds nothing that could do more.
/// </para>
/// <para>
/// A pack is added in two steps with the person between them: <see cref="PreviewPackAsync"/>
/// says what would be added, and only <see cref="AddPackAsync"/>, after the view's dialog, adds.
/// </para>
/// </remarks>
public sealed class WorkspaceViewModel : ObservableObject
{
    private readonly StarterPackService _packs;
    private readonly PinnedSearchService _pins;
    private readonly ISavedSearchRepository _searches;
    private readonly SearchRequest _request;
    private readonly IClock _clock;
    private string _pinsCaption = string.Empty;
    private string _pinMessage = string.Empty;
    private bool _canPinMore = true;
    private bool _isBusy;

    public WorkspaceViewModel(
        StarterPackService packs,
        PinnedSearchService pins,
        ISavedSearchRepository searches,
        SearchRequest request,
        IClock clock)
    {
        _packs = packs;
        _pins = pins;
        _searches = searches;
        _request = request;
        _clock = clock;
        Packs = StarterPackCatalog.All
            .Select(pack => new StarterPackCardViewModel(pack.Id, pack.Name, pack.Summary))
            .ToArray();
        PinCommand = new AsyncRelayCommand<Guid>(PinAsync, _ => !IsBusy);
        UnpinCommand = new AsyncRelayCommand<Guid>(UnpinAsync, _ => !IsBusy);
    }

    public IReadOnlyList<StarterPackCardViewModel> Packs { get; }

    public ObservableCollection<PinnedSearchTileViewModel> Pins { get; } = [];

    public ObservableCollection<UnpinnedSearchViewModel> OtherSearches { get; } = [];

    public AsyncRelayCommand<Guid> PinCommand { get; }

    public AsyncRelayCommand<Guid> UnpinCommand { get; }

    public bool HasPins => Pins.Count > 0;

    public bool HasNoPins => Pins.Count == 0;

    public bool HasOtherSearches => OtherSearches.Count > 0;

    public static int MaxPinned => SavedSearch.MaxPinned;

    /// <summary>Where the counts come from and when they were made, so no tile looks live.</summary>
    public string PinsCaption
    {
        get => _pinsCaption;
        private set => SetProperty(ref _pinsCaption, value);
    }

    /// <summary>The answer to pressing Pin, shown beside the list the button is in.</summary>
    public string PinMessage
    {
        get => _pinMessage;
        private set
        {
            if (SetProperty(ref _pinMessage, value))
            {
                OnPropertyChanged(nameof(HasPinMessage));
            }
        }
    }

    public bool HasPinMessage => !string.IsNullOrEmpty(PinMessage);

    public bool CanPinMore
    {
        get => _canPinMore;
        private set => SetProperty(ref _canPinMore, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                PinCommand.NotifyCanExecuteChanged();
                UnpinCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public Task InitializeAsync() => ReloadAsync();

    /// <summary>What adding a pack would do right now. Saves nothing.</summary>
    public Task<StarterPackPreview> PreviewPackAsync(string packId) => _packs.PreviewAsync(packId);

    /// <summary>
    /// Adds a pack after the person agreed in the preview dialog, and says what happened on its card.
    /// </summary>
    public async Task AddPackAsync(string packId)
    {
        var card = Packs.First(item => item.Id == packId);
        IsBusy = true;
        try
        {
            var outcome = await _packs.AddAsync(packId).ConfigureAwait(true);
            card.Result = DescribeOutcome(outcome);
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            card.Result = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Leaves the saved search for Search to run when it opens. The view then navigates there.
    /// </summary>
    public void OpenInSearch(Guid savedSearchId) => _request.Ask(savedSearchId);

    /// <summary>
    /// Writes what adding a pack did as one calm paragraph.
    /// </summary>
    /// <remarks>
    /// Rules are always said to be switched off when any were added, because the one thing a
    /// person must not come away believing is that DeskAI started doing something new.
    /// </remarks>
    internal static string DescribeOutcome(StarterPackOutcome outcome)
    {
        var parts = new List<string>();
        if (outcome.StoppedBecause is not null)
        {
            parts.Add($"DeskAI stopped safely: {outcome.StoppedBecause}");
        }

        if (outcome.AddedSearches.Count == 0 && outcome.AddedRules.Count == 0)
        {
            if (outcome.StoppedBecause is null)
            {
                parts.Add("Nothing added — you already have everything in this pack.");
            }
        }
        else
        {
            parts.Add(outcome.AddedRules.Count == 0
                ? $"Added {Count(outcome.AddedSearches.Count, "search", "searches")}."
                : $"Added {Count(outcome.AddedSearches.Count, "search", "searches")} and {Count(outcome.AddedRules.Count, "rule", "rules")}.");
        }

        var alreadyHad = outcome.Skipped
            .Where(item => item.SkipReason!.StartsWith("You already have a ", StringComparison.Ordinal))
            .Select(item => item.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (alreadyHad.Length > 0 && (outcome.AddedSearches.Count > 0 || outcome.AddedRules.Count > 0))
        {
            parts.Add($"Skipped {alreadyHad.Length} you already had: {string.Join(", ", alreadyHad)}.");
        }

        var overLimit = outcome.Skipped.Count(item => !item.SkipReason!.StartsWith("You already have a ", StringComparison.Ordinal));
        if (overLimit > 0)
        {
            parts.Add($"{Count(overLimit, "search was", "searches were")} not added — you already have {SavedSearch.MaxSavedSearches} saved searches.");
        }

        if (outcome.AddedButNotPinned.Count > 0)
        {
            parts.Add($"Not pinned, because {SavedSearch.MaxPinned} are already pinned: {string.Join(", ", outcome.AddedButNotPinned)}.");
        }

        if (outcome.AddedRules.Count > 0)
        {
            parts.Add("The rules are switched off — turn them on in Automatic tasks.");
        }

        return string.Join(" ", parts);
    }

    internal static string DescribeCount(PinnedCount count) => count.Kind switch
    {
        PinnedCountKind.NoFolders => "No folders connected",
        PinnedCountKind.NotUnderstood => "Search not understood",
        PinnedCountKind.AtLimit => $"{count.Files}+ files",
        _ => count.Files == 1 ? "1 file" : $"{count.Files} files",
    };

    private static string Count(int value, string one, string many) => value == 1 ? $"1 {one}" : $"{value} {many}";

    private async Task PinAsync(Guid savedSearchId)
    {
        IsBusy = true;
        try
        {
            PinMessage = await _pins.PinAsync(savedSearchId).ConfigureAwait(true)
                ? string.Empty
                : $"You can pin up to {SavedSearch.MaxPinned} searches. Unpin one to make room.";
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            PinMessage = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task UnpinAsync(Guid savedSearchId)
    {
        IsBusy = true;
        try
        {
            await _pins.UnpinAsync(savedSearchId).ConfigureAwait(true);
            PinMessage = string.Empty;
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            PinMessage = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadAsync()
    {
        var all = await _searches.ListAsync().ConfigureAwait(true);

        Pins.Clear();
        foreach (var saved in all.Where(item => item.IsPinned))
        {
            string count;
            try
            {
                count = DescribeCount(await _pins.CountAsync(saved).ConfigureAwait(true));
            }
            catch (Exception exception) when (IsExpectedFailure(exception))
            {
                // One tile failing to count must not blank the others.
                count = "Could not count";
            }

            Pins.Add(new PinnedSearchTileViewModel(saved.Id, saved.Name, count));
        }

        OtherSearches.Clear();
        foreach (var saved in all.Where(item => !item.IsPinned))
        {
            OtherSearches.Add(new UnpinnedSearchViewModel(saved.Id, saved.Name, saved.Phrase));
        }

        CanPinMore = Pins.Count < SavedSearch.MaxPinned;
        var now = _clock.UtcNow.ToLocalTime();
        PinsCaption = $"Counted at {now:t} from what DeskAI remembers. Refresh a folder in Search to update it.";
        OnPropertyChanged(nameof(HasPins));
        OnPropertyChanged(nameof(HasNoPins));
        OnPropertyChanged(nameof(HasOtherSearches));
    }

    private static bool IsExpectedFailure(Exception exception) =>
        exception is IOException
            or InvalidOperationException
            or FormatException
            or System.Data.Common.DbException;
}
