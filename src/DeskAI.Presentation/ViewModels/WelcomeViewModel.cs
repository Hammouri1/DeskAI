using CommunityToolkit.Mvvm.ComponentModel;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Roots;

namespace DeskAI.App.ViewModels;

/// <summary>One page of the welcome: a title, a line under it, and ticked promises (page 2 only).</summary>
public sealed record WelcomePage(string Title, string Body, IReadOnlyList<string> Promises);

/// <summary>
/// The first-run welcome's three pages, and the folder a person chose to connect from it.
/// </summary>
/// <remarks>
/// It cannot connect anything by itself. Choosing a folder only records it; the window then asks
/// Home's "Connect your …?" question, and only that question's yes calls
/// <see cref="ConnectChosenAsync"/>, which is Home's own connect. It holds no settings store:
/// opening it again from Privacy and AI never changes whether DeskAI remembers greeting.
/// </remarks>
public sealed class WelcomeViewModel(PersonalFoldersViewModel folders, IAiSettingsRepository aiSettings) : ObservableObject
{
    public const string AiOffLine = "AI is off. You can turn it on later in Privacy and AI.";

    private readonly IAiSettingsRepository _aiSettings = aiSettings;
    private int _pageIndex;
    private string _aiLine = AiOffLine;

    public static IReadOnlyList<WelcomePage> Pages { get; } =
    [
        new("Welcome to DeskAI", "Find your files and keep them tidy.", []),
        new("You stay in charge", string.Empty,
        [
            "DeskAI sees nothing until you connect a folder.",
            "It only works in your Desktop, Downloads, Documents, and Pictures.",
            "Nothing moves until you see it and say yes.",
            "You can put things back.",
        ]),
        new("Let's start", "Connect a folder to begin. DeskAI asks once more before connecting.", []),
    ];

    public PersonalFoldersViewModel Folders { get; } = folders;

    public int PageIndex
    {
        get => _pageIndex;
        private set
        {
            if (SetProperty(ref _pageIndex, value))
            {
                OnPropertyChanged(nameof(Current));
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(IsLastPage));
                OnPropertyChanged(nameof(NextText));
                OnPropertyChanged(nameof(PageNumberText));
                OnPropertyChanged(nameof(Dots));
            }
        }
    }

    public WelcomePage Current => Pages[_pageIndex];

    public bool CanGoBack => _pageIndex > 0;

    public bool IsLastPage => _pageIndex == Pages.Count - 1;

    public string NextText => IsLastPage ? "Done" : "Next";

    /// <summary>What a screen reader says for the row of dots.</summary>
    public string PageNumberText => $"Page {_pageIndex + 1} of {Pages.Count}";

    /// <summary>One dot per page; true for the page on screen.</summary>
    public IReadOnlyList<bool> Dots => [.. Enumerable.Range(0, Pages.Count).Select(index => index == _pageIndex)];

    /// <summary>
    /// The AI line on the last page. Read from the saved choice, so a reopened welcome never
    /// claims AI is off while it is on.
    /// </summary>
    public string AiLine
    {
        get => _aiLine;
        private set => SetProperty(ref _aiLine, value);
    }

    public PersonalFolderKind? ChosenFolder { get; private set; }

    /// <summary>Starts on page one with nothing chosen, and reads the folder rows and the AI choice afresh.</summary>
    public async Task OpenAsync()
    {
        PageIndex = 0;
        ChosenFolder = null;
        await Folders.ReloadAsync().ConfigureAwait(true);
        try
        {
            var state = ShellViewModel.DescribeAi(await _aiSettings.LoadAsync().ConfigureAwait(true));
            AiLine = state == "AI off" ? AiOffLine : $"{state}. You can change this in Privacy and AI.";
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.Data.Common.DbException
            or IOException)
        {
            // Never guess "off": say where the choice lives instead.
            AiLine = "You can choose whether to use AI in Privacy and AI.";
        }
    }

    /// <returns>True when Done was pressed on the last page, so the pop-up closes.</returns>
    public bool Next()
    {
        if (IsLastPage)
        {
            return true;
        }

        PageIndex++;
        return false;
    }

    public void Back()
    {
        if (CanGoBack)
        {
            PageIndex--;
        }
    }

    /// <summary>Records the Connect press. Nothing is connected here.</summary>
    public void Choose(PersonalFolderKind kind) => ChosenFolder = kind;

    /// <summary>Home's own connect, called only after the "Connect your …?" question's yes.</summary>
    public async Task<Guid?> ConnectChosenAsync() =>
        ChosenFolder is { } kind ? await Folders.ConnectAsync(kind).ConfigureAwait(true) : null;
}
