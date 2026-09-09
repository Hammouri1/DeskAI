using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeskAI.Core.Abstractions;
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
    private readonly IClock _clock;
    private string _phrase = string.Empty;
    private string _statusTitle = "Search your connected folders";
    private string _statusMessage =
        "Try \"photos from last month\" or \"documents over 10 mb\". DeskAI reads only the folders you connected.";
    private string _scopeMessage = string.Empty;
    private bool _isBusy;
    private bool _hasSearched;

    public SearchViewModel(FileSearchService search, IClock clock)
    {
        _search = search;
        _clock = clock;
        SearchCommand = new AsyncRelayCommand(RunAsync, () => !IsBusy);
    }

    public ObservableCollection<string> Chips { get; } = [];

    public ObservableCollection<SearchResultViewModel> Results { get; } = [];

    public AsyncRelayCommand SearchCommand { get; }

    /// <summary>Bounded in the view as well, so an over-long phrase cannot be submitted.</summary>
    public static int MaxPhraseLength => NaturalLanguageQueryTranslator.MaxInputLength;

    public string Phrase
    {
        get => _phrase;
        set => SetProperty(ref _phrase, value);
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
            }
        }
    }

    public bool HasResults => Results.Count > 0;

    public bool HasChips => Chips.Count > 0;

    /// <summary>True only after a search that found nothing, so the first visit stays calm.</summary>
    public bool ShowsNothingFound => _hasSearched && Results.Count == 0;

    private async Task RunAsync()
    {
        IsBusy = true;
        try
        {
            var outcome = await _search.SearchAsync(Phrase, _clock.UtcNow).ConfigureAwait(true);
            Apply(outcome);
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
            StatusMessage =
                "Connect a folder in Organize first. DeskAI can only search folders you have chosen.";
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
                : "These are names, sizes, and dates DeskAI remembered. Nothing has been opened or changed.";

            ScopeMessage = outcome.FoldersSearched == 1
                ? "Searched 1 connected folder."
                : $"Searched {outcome.FoldersSearched} connected folders.";
        }

        RaiseListChanges();
    }

    private void Reset()
    {
        Chips.Clear();
        Results.Clear();
    }

    private void RaiseListChanges()
    {
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasChips));
        OnPropertyChanged(nameof(ShowsNothingFound));
    }
}
