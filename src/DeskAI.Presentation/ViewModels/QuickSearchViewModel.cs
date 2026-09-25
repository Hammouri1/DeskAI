using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Classification;
using DeskAI.Core.QuickSearch;
using Fact = DeskAI.Core.QuickSearch.NameFact;

namespace DeskAI.App.ViewModels;

/// <summary>One row in the bar: a file, where it is, and what Enter does with it.</summary>
public sealed class QuickSearchRowViewModel(QuickSearchRow row) : ObservableObject
{
    /// <summary>A Words inside snippet is cut to one short line; the full text stays on Search.</summary>
    private const int MaxSnippet = 110;

    private bool _isSelected;

    public QuickSearchRow Row { get; } = row;

    public string Name => Row.Name;

    public string Where => Row.Section is { Length: > 0 } section ? $"{Row.Where} · {section}" : Row.Where;

    public string Snippet { get; } = OneLine(row.Snippet);

    public bool HasSnippet => Snippet.Length > 0;

    public string ActionText => Row.Choice == OpenChoice.Open ? "Open" : "Show in folder";

    /// <summary>The coloured tile's key: document blue, PDF red, picture orange, video violet, anything else grey.</summary>
    public string KindKey { get; } = KindOf(row);

    /// <summary>The short word on the tile.</summary>
    public string KindLabel => KindKey.ToUpperInvariant();

    /// <summary>What Enter does on the selected row.</summary>
    public string EnterHint => ActionText + " ↵";

    public string Glyph => Row.Category switch
    {
        FileCategory.Images or FileCategory.Screenshots => "",
        FileCategory.Videos => "",
        FileCategory.Audio => "",
        _ => "",
    };

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private static string KindOf(QuickSearchRow row) => row.Category switch
    {
        _ when row.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) => "pdf",
        FileCategory.Images or FileCategory.Screenshots => "img",
        FileCategory.Videos => "vid",
        FileCategory.Documents or FileCategory.Presentations or FileCategory.Spreadsheets => "doc",
        _ => "file",
    };

    private static string OneLine(string text)
    {
        var flat = string.Join(' ', text.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return flat.Length <= MaxSnippet ? flat : flat[..(MaxSnippet - 1)].TrimEnd() + "…";
    }
}

/// <summary>
/// The quick search bar: the buddy, the box, two short groups of results, and Enter.
/// </summary>
/// <remarks>
/// <para>
/// Each keystroke cancels whatever the last one started, then waits a moment and looks up names,
/// then waits a little more and looks inside files. Results from an old keystroke are never shown
/// under new words, because every look checks its own cancellation before touching the rows.
/// </para>
/// <para>
/// Stores nothing typed, found, or read. It holds the launcher (ADR 0047), the two read-only
/// searches, the buddy choice, and the Search request; nothing that changes a permission, talks
/// to AI, or changes a file. Tests assert it.
/// </para>
/// </remarks>
public sealed class QuickSearchViewModel : ObservableObject, IDisposable
{
    private readonly QuickSearchService _search;
    private readonly QuickSearchSettingsService _settings;
    private readonly IFileLauncher _launcher;
    private readonly SearchRequest _request;
    private readonly IClock _clock;
    private readonly QuickSearchTiming _timing;
    private CancellationTokenSource _typing = new();
    private string _phrase = string.Empty;
    private QuickSearchRowViewModel? _selected;
    private SearchBuddy _buddy = SearchBuddy.Sparky;
    private BuddyMood _mood = BuddyMood.Idle;
    private BuddyMood _moodBeforePetting = BuddyMood.Idle;
    private int _found;
    private string _nameFact = string.Empty;
    private string _insideFact = string.Empty;
    private string _openMessage = string.Empty;
    private bool _isLookingInside;
    private bool _showsSeeMore;
    private bool _showsOpenDeskAi;
    private bool _showsInsideGroup;

    public QuickSearchViewModel(
        QuickSearchService search,
        QuickSearchSettingsService settings,
        IFileLauncher launcher,
        SearchRequest request,
        IClock clock,
        QuickSearchTiming timing)
    {
        _search = search;
        _settings = settings;
        _launcher = launcher;
        _request = request;
        _clock = clock;
        _timing = timing;
    }

    /// <summary>Asks the window to open DeskAI on a page: "search" or "dashboard".</summary>
    public event EventHandler<string>? OpenDeskAiRequested;

    public static IReadOnlyList<string> Examples { get; } = ["pdf from last week", "photos from this month", "big videos"];

    public ObservableCollection<QuickSearchRowViewModel> NameRows { get; } = [];

    public ObservableCollection<QuickSearchRowViewModel> InsideRows { get; } = [];

    /// <summary>The latest look started by typing. Tests await it; the window never needs to.</summary>
    public Task Pending { get; private set; } = Task.CompletedTask;

    public string Phrase
    {
        get => _phrase;
        set
        {
            if (SetProperty(ref _phrase, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(ShowsExamples));
                Pending = LookAsync(Restart());
            }
        }
    }

    public bool ShowsExamples => string.IsNullOrWhiteSpace(_phrase);

    public QuickSearchRowViewModel? Selected
    {
        get => _selected;
        private set
        {
            if (_selected is not null)
            {
                _selected.IsSelected = false;
            }

            SetProperty(ref _selected, value);
            if (_selected is not null)
            {
                _selected.IsSelected = true;
            }
        }
    }

    public SearchBuddy Buddy
    {
        get => _buddy;
        private set
        {
            if (SetProperty(ref _buddy, value))
            {
                OnPropertyChanged(nameof(BuddyLine));
            }
        }
    }

    public BuddyMood Mood
    {
        get => _mood;
        private set
        {
            if (SetProperty(ref _mood, value))
            {
                OnPropertyChanged(nameof(BuddyLine));
            }
        }
    }

    public string BuddyLine => SearchBuddyLines.Line(_buddy, _mood, _found);

    public string NameFact
    {
        get => _nameFact;
        private set
        {
            if (SetProperty(ref _nameFact, value))
            {
                OnPropertyChanged(nameof(HasNameFact));
            }
        }
    }

    public bool HasNameFact => _nameFact.Length > 0;

    public string InsideFact
    {
        get => _insideFact;
        private set
        {
            if (SetProperty(ref _insideFact, value))
            {
                OnPropertyChanged(nameof(HasInsideFact));
            }
        }
    }

    public bool HasInsideFact => _insideFact.Length > 0;

    public string OpenMessage
    {
        get => _openMessage;
        private set
        {
            if (SetProperty(ref _openMessage, value))
            {
                OnPropertyChanged(nameof(HasOpenMessage));
            }
        }
    }

    public bool HasOpenMessage => _openMessage.Length > 0;

    public bool IsLookingInside { get => _isLookingInside; private set => SetProperty(ref _isLookingInside, value); }

    /// <summary>Whether the Words inside heading shows: while looking, or when it found something.</summary>
    public bool ShowsInsideGroup { get => _showsInsideGroup; private set => SetProperty(ref _showsInsideGroup, value); }

    public bool ShowsSeeMore { get => _showsSeeMore; private set => SetProperty(ref _showsSeeMore, value); }

    public bool ShowsOpenDeskAi { get => _showsOpenDeskAi; private set => SetProperty(ref _showsOpenDeskAi, value); }

    /// <summary>The shortcut was pressed: a clean bar with the chosen buddy greeting.</summary>
    public async Task ShowAsync()
    {
        _typing.Cancel();
        Buddy = (await _settings.LoadAsync().ConfigureAwait(true)).Buddy;
        Clear();
        _phrase = string.Empty;
        OnPropertyChanged(nameof(Phrase));
        OnPropertyChanged(nameof(ShowsExamples));
        Mood = BuddyMood.Idle;
        OnPropertyChanged(nameof(BuddyLine));
    }

    /// <summary>Esc, a click outside, or opening a file: stops any look and forgets the words.</summary>
    public void Hide()
    {
        _typing.Cancel();
        _phrase = string.Empty;
        OnPropertyChanged(nameof(Phrase));
        OnPropertyChanged(nameof(ShowsExamples));
        Clear();
        Mood = BuddyMood.Idle;
    }

    public Task ChooseExampleAsync(string example)
    {
        Phrase = example;
        return Pending;
    }

    public void MoveSelection(int delta)
    {
        var all = NameRows.Concat(InsideRows).ToArray();
        if (all.Length == 0)
        {
            return;
        }

        var index = _selected is null ? 0 : Array.IndexOf(all, _selected) + delta;
        Selected = all[Math.Clamp(index, 0, all.Length - 1)];
    }

    /// <returns>True when the bar should hide (the file was opened or shown).</returns>
    public async Task<bool> ActivateAsync(QuickSearchRowViewModel? row, bool showInFolder)
    {
        if (row is null)
        {
            return false;
        }

        OpenMessage = string.Empty;
        var result = showInFolder || row.Row.Choice != OpenChoice.Open
            ? await _launcher.ShowInFolderAsync(row.Row.RootId, row.Row.RelativePath).ConfigureAwait(true)
            : await _launcher.OpenAsync(row.Row.RootId, row.Row.RelativePath).ConfigureAwait(true);
        if (!result.Done)
        {
            OpenMessage = $"DeskAI couldn't open it: {result.Reason}";
        }

        return result.Done;
    }

    /// <summary>Clicking the buddy: a happy moment, then back to how it was. Does nothing else.</summary>
    public async Task PetBuddyAsync()
    {
        if (_mood != BuddyMood.Happy)
        {
            _moodBeforePetting = _mood;
        }

        Mood = BuddyMood.Happy;
        await Task.Delay(_timing.HappyFor).ConfigureAwait(true);
        if (_mood == BuddyMood.Happy)
        {
            Mood = _moodBeforePetting;
        }
    }

    public void SeeMoreInDeskAi()
    {
        if (!string.IsNullOrWhiteSpace(_phrase))
        {
            _request.AskPhrase(_phrase.Trim());
        }

        OpenDeskAiRequested?.Invoke(this, "search");
    }

    public void OpenDeskAi() => OpenDeskAiRequested?.Invoke(this, "dashboard");

    public void Dispose()
    {
        _typing.Cancel();
        _typing.Dispose();
    }

    private CancellationToken Restart()
    {
        _typing.Cancel();
        _typing.Dispose();
        _typing = new CancellationTokenSource();
        return _typing.Token;
    }

    private async Task LookAsync(CancellationToken token)
    {
        // Runs synchronously up to the first await, so an old "Looking inside files…" never
        // outlives the keystroke that replaced it.
        IsLookingInside = false;
        try
        {
            if (string.IsNullOrWhiteSpace(_phrase))
            {
                Clear();
                Mood = BuddyMood.Idle;
                return;
            }

            await Task.Delay(_timing.NameDelay, token).ConfigureAwait(true);
            Mood = BuddyMood.Thinking;
            var words = _phrase;
            var byName = await _search.FindByNameAsync(words, _clock.UtcNow, token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            ShowNames(byName);

            if (!byName.AnyFolderReadsInside || byName.Fact is Fact.NoFolders)
            {
                if (byName.Fact is not Fact.NoFolders)
                {
                    // The spec pairs this line with See more, which opens Search, where it is allowed.
                    InsideFact = "To find words inside files too, allow it for a folder on the Search page.";
                    ShowsSeeMore = true;
                }

                Settle();
                return;
            }

            var untilInside = _timing.InsideDelay - _timing.NameDelay;
            if (untilInside > TimeSpan.Zero)
            {
                await Task.Delay(untilInside, token).ConfigureAwait(true);
            }

            IsLookingInside = true;
            ShowsInsideGroup = true;
            InsideFact = "Looking inside files…";
            var inside = await _search.FindInsideAsync(words, _clock.UtcNow,
                byName.Rows.Select(row => row.Key).ToHashSet(StringComparer.Ordinal), token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            ShowInside(inside, byName.Rows.Count);
            Settle();
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke or Hide took over; it owns the rows now.
        }
    }

    private void ShowNames(NameResults results)
    {
        NameRows.Clear();
        InsideRows.Clear();
        foreach (var row in results.Rows)
        {
            NameRows.Add(new QuickSearchRowViewModel(row));
        }

        Selected = NameRows.FirstOrDefault();
        OpenMessage = string.Empty;
        ShowsOpenDeskAi = results.Fact is Fact.NoFolders;
        ShowsSeeMore = results.Fact is Fact.MoreThanShown;
        ShowsInsideGroup = false;
        InsideFact = string.Empty;
        NameFact = results.Fact switch
        {
            Fact.NoFolders => "Connect a folder in DeskAI first. Quick search looks only in folders you connected.",
            Fact.NotUnderstood => "Try a name, a kind like \"pdf\", or a time like \"last week\".",
            Fact.MoreThanShown => $"Showing the first {QuickSearchService.MaxRows} by name.",
            _ => string.Empty,
        };
        _found = NameRows.Count;
    }

    private void ShowInside(InsideResults results, int nameCount)
    {
        IsLookingInside = false;
        if (!results.WasSearched)
        {
            ShowsInsideGroup = false;
            InsideFact = string.Empty;
            return;
        }

        foreach (var row in results.Rows)
        {
            InsideRows.Add(new QuickSearchRowViewModel(row));
        }

        Selected ??= InsideRows.FirstOrDefault();
        ShowsInsideGroup = InsideRows.Count > 0;
        var read = results.FilesRead == 1 ? "1 file" : $"{results.FilesRead} files";
        var what = nameCount > 0 ? "Nothing else found" : "Nothing found";
        InsideFact = (InsideRows.Count, results.ReachedLimit) switch
        {
            (0, true) => $"{what} in the first {read} DeskAI checked.",
            (0, false) => $"{what} inside the {read} DeskAI checked.",
            (_, true) => "There may be more.",
            _ => string.Empty,
        };
        ShowsSeeMore |= results.ReachedLimit;
        _found = NameRows.Count + InsideRows.Count;
    }

    /// <summary>The buddy's mood and the "nothing matched" line once both looks are done.</summary>
    private void Settle()
    {
        IsLookingInside = false;
        _found = NameRows.Count + InsideRows.Count;
        if (_found == 0 && NameFact.Length == 0)
        {
            NameFact = "Nothing matched in your connected folders.";
        }

        Mood = _found > 0 ? BuddyMood.Found : BuddyMood.Nothing;
        OnPropertyChanged(nameof(BuddyLine));
    }

    private void Clear()
    {
        NameRows.Clear();
        InsideRows.Clear();
        Selected = null;
        NameFact = string.Empty;
        InsideFact = string.Empty;
        OpenMessage = string.Empty;
        IsLookingInside = false;
        ShowsInsideGroup = false;
        ShowsSeeMore = false;
        ShowsOpenDeskAi = false;
        _found = 0;
    }
}
