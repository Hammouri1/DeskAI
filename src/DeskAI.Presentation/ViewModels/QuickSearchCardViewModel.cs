using CommunityToolkit.Mvvm.ComponentModel;
using DeskAI.App.Services;
using DeskAI.Core.QuickSearch;

namespace DeskAI.App.ViewModels;

/// <summary>One buddy on the Quick search card: its face, and what the stage shows when it is chosen.</summary>
public sealed class BuddyTileViewModel(SearchBuddy buddy) : ObservableObject
{
    private bool _isChosen;

    public SearchBuddy Buddy { get; } = buddy;

    public string Name { get; } = SearchBuddyLines.Name(buddy);

    /// <summary>What the buddy says on the stage: its hello.</summary>
    public string HelloLine { get; } = SearchBuddyLines.Line(buddy, BuddyMood.Idle);

    /// <summary>The stage's colours, named as in the mockup: night, study, lab, forest, sea, meadow, dusk.</summary>
    public string Stage { get; } = buddy switch
    {
        SearchBuddy.Archie => "study",
        SearchBuddy.Pip => "lab",
        SearchBuddy.Fetch => "forest",
        SearchBuddy.Inky => "sea",
        SearchBuddy.Mochi => "meadow",
        SearchBuddy.Paige => "dusk",
        _ => "night",
    };

    /// <summary>What a screen reader says for the tile's button, e.g. "Choose Sparky".</summary>
    public string ChooseName => $"Choose {Name}";

    public bool IsChosen { get => _isChosen; set => SetProperty(ref _isChosen, value); }
}

/// <summary>The Quick search card on My workspace: the switch, the shortcut, the buddy stage and faces, and a shortcut problem.</summary>
public sealed class QuickSearchCardViewModel(QuickSearchSettingsService settings, QuickSearchSwitch quickSwitch, IQuickSearchHotKey hotKey, BuddyMotion motion) : ObservableObject
{
    public const string WorksWhenLine =
        "Works while DeskAI is open or near the clock. Closing the window keeps it near the clock; quit from the icon's menu there.";

    public const string MotionLine = "Turn this off to keep your buddy and the search bar still.";

    private readonly QuickSearchSettingsService _settings = settings;
    private readonly QuickSearchSwitch _switch = quickSwitch;
    private readonly IQuickSearchHotKey _hotKey = hotKey;
    private bool _isOn = true;
    private string _shortcutProblem = string.Empty;
    private QuickSearchShortcut _shortcut = QuickSearchShortcuts.Default;
    private bool _letsBuddyMove = true;
    private BuddyTileViewModel? _chosen;

    /// <summary>The same line, for the page to bind to.</summary>
    public static string WorksWhen => WorksWhenLine;

    /// <summary>The motion switch's line, for the page to bind to.</summary>
    public static string MotionText => MotionLine;

    /// <summary>What the page's buddies follow.</summary>
    public BuddyMotion Motion { get; } = motion;

    public bool LetsBuddyMove { get => _letsBuddyMove; private set => SetProperty(ref _letsBuddyMove, value); }

    /// <summary>The drop-down's choices, in the list's order.</summary>
    public static IReadOnlyList<string> ShortcutChoices { get; } = [.. QuickSearchShortcuts.All.Select(QuickSearchShortcuts.Text)];

    public QuickSearchShortcut Shortcut
    {
        get => _shortcut;
        private set
        {
            if (SetProperty(ref _shortcut, value))
            {
                OnPropertyChanged(nameof(ShortcutIndex));
                OnPropertyChanged(nameof(SwitchHeader));
            }
        }
    }

    /// <summary>The drop-down's selected row: the enum's values are 0, 1, 2 in the list's order.</summary>
    public int ShortcutIndex => (int)_shortcut;

    public string SwitchHeader => $"Press {QuickSearchShortcuts.Text(_shortcut)} to find a file";

    public IReadOnlyList<BuddyTileViewModel> Buddies { get; } =
        Enum.GetValues<SearchBuddy>().Select(buddy => new BuddyTileViewModel(buddy)).ToArray();

    /// <summary>The buddy on the stage. Sparky until the stored choice is read.</summary>
    public BuddyTileViewModel Chosen => _chosen ?? Buddies[0];

    /// <summary>The chosen face's place in the row: the buddies are listed in the enum's order.</summary>
    public int ChosenIndex => (int)Chosen.Buddy;

    public bool IsOn { get => _isOn; private set => SetProperty(ref _isOn, value); }

    public string ShortcutProblem
    {
        get => _shortcutProblem;
        private set
        {
            if (SetProperty(ref _shortcutProblem, value))
            {
                OnPropertyChanged(nameof(HasShortcutProblem));
            }
        }
    }

    public bool HasShortcutProblem => _shortcutProblem.Length > 0;

    public async Task InitializeAsync()
    {
        var stored = await _settings.LoadAsync().ConfigureAwait(true);
        IsOn = stored.IsOn;
        Shortcut = stored.Shortcut;
        LetsBuddyMove = stored.LetsBuddyMove;
        Choose(stored.Buddy);
        Report(_hotKey.State);
    }

    public async Task SetOnAsync(bool on)
    {
        IsOn = on;
        Report(await _switch.SetOnAsync(on).ConfigureAwait(true));
    }

    public async Task SetShortcutAsync(QuickSearchShortcut shortcut)
    {
        Shortcut = shortcut;
        Report(await _switch.SetShortcutAsync(shortcut).ConfigureAwait(true));
    }

    public async Task SetMotionAsync(bool moves)
    {
        LetsBuddyMove = moves;
        await _switch.SetMotionAsync(moves).ConfigureAwait(true);
    }

    public async Task ChooseBuddyAsync(SearchBuddy buddy)
    {
        await _settings.SetBuddyAsync(buddy).ConfigureAwait(true);
        Choose(buddy);
    }

    private void Choose(SearchBuddy buddy)
    {
        foreach (var tile in Buddies)
        {
            tile.IsChosen = tile.Buddy == buddy;
        }

        _chosen = Buddies.First(tile => tile.IsChosen);
        OnPropertyChanged(nameof(Chosen));
        OnPropertyChanged(nameof(ChosenIndex));
    }

    private void Report(HotKeyState state) => ShortcutProblem = state == HotKeyState.TakenByAnotherProgram
        ? $"Another program already uses {QuickSearchShortcuts.Text(_shortcut)}. Pick another shortcut above."
        : string.Empty;
}
