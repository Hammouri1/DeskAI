using CommunityToolkit.Mvvm.ComponentModel;
using DeskAI.App.Services;
using DeskAI.Core.QuickSearch;

namespace DeskAI.App.ViewModels;

/// <summary>One buddy's tile on the Quick search card.</summary>
public sealed class BuddyTileViewModel(SearchBuddy buddy) : ObservableObject
{
    private bool _isChosen;

    public SearchBuddy Buddy { get; } = buddy;

    public string Name { get; } = SearchBuddyLines.Name(buddy);

    /// <summary>What a screen reader says for the tile's button, e.g. "Choose Sparky".</summary>
    public string ChooseName => $"Choose {Name}";

    public bool IsChosen { get => _isChosen; set => SetProperty(ref _isChosen, value); }
}

/// <summary>The Quick search card on My workspace: the switch, the seven buddies, and a shortcut problem.</summary>
public sealed class QuickSearchCardViewModel(QuickSearchSettingsService settings, QuickSearchSwitch quickSwitch, IQuickSearchHotKey hotKey) : ObservableObject
{
    public const string WorksWhenLine =
        "Works while DeskAI is open or near the clock. Closing the window keeps it near the clock; quit from the icon's menu there.";

    private readonly QuickSearchSettingsService _settings = settings;
    private readonly QuickSearchSwitch _switch = quickSwitch;
    private readonly IQuickSearchHotKey _hotKey = hotKey;
    private bool _isOn = true;
    private string _shortcutProblem = string.Empty;

    /// <summary>The same line, for the page to bind to.</summary>
    public static string WorksWhen => WorksWhenLine;

    public IReadOnlyList<BuddyTileViewModel> Buddies { get; } =
        Enum.GetValues<SearchBuddy>().Select(buddy => new BuddyTileViewModel(buddy)).ToArray();

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
        Choose(stored.Buddy);
        Report(_hotKey.State);
    }

    public async Task SetOnAsync(bool on)
    {
        IsOn = on;
        Report(await _switch.SetOnAsync(on).ConfigureAwait(true));
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
    }

    private void Report(HotKeyState state) => ShortcutProblem = state == HotKeyState.TakenByAnotherProgram
        ? "Another program already uses Ctrl + Alt + Space, so quick search can't listen for it."
        : string.Empty;
}
