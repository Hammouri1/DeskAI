using DeskAI.App.Navigation;
using DeskAI.App.Services;
using DeskAI.App.ViewModels;
using DeskAI.Core.Rules;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskAI.App;

public sealed partial class MainWindow : Window
{
    private readonly INavigationService? _navigationService;
    private readonly ShellViewModel? _shell;
    private readonly IFindingNotifier? _notifier;
    private readonly BackgroundPresenceController? _presence;
    private readonly WelcomeViewModel? _welcome;

    /// <summary>Only one pop-up can be open in a window, so a second request while it is open does nothing.</summary>
    private bool _welcomeOpen;

    /// <summary>
    /// Whether the "DeskAI is still running" notice has already been shown this run.
    /// </summary>
    /// <remarks>
    /// In memory, and deliberately not stored. There is no spare field on the settings and no
    /// general place to keep a single flag, so remembering it across runs would mean a database
    /// change for a one-line nudge. Once per run is also the kinder behaviour: someone who turns
    /// the mode on, never closes the window that session, and comes back a week later is still
    /// told where DeskAI went rather than left hunting for it.
    /// </remarks>
    private bool _toldThemWhereItWent;

    /// <summary>Set once "Quit DeskAI" was chosen. See <see cref="AllowTheRealClose"/>.</summary>
    private bool _quitting;

    public MainWindow(
        ShellViewModel viewModel,
        INavigationService navigationService,
        IFindingNotifier notifier,
        BackgroundPresenceController presence,
        WelcomeViewModel welcome)
    {
        _welcome = welcome;
        InitializeComponent();
        Title = "DeskAI";
        ApplyWindowIcon();
        _shell = viewModel;
        RootNavigation.DataContext = viewModel;
        _navigationService = navigationService;
        _notifier = notifier;
        _presence = presence;
        _navigationService.Initialize(ContentFrame);
        RootNavigation.SelectedItem = RootNavigation.MenuItems
            .OfType<NavigationViewItem>()
            .First(item => item.Tag is "dashboard");
        _navigationService.Navigate("dashboard");
        _shell.ShowPage("dashboard");
        RootNavigation.ActualThemeChanged += (_, _) => ReportTheme();
        AppWindow.Closing += OnClosing;
        RootNavigation.Loaded += OnFirstLoaded;
        _ = RefreshScopeAsync();
    }

    /// <summary>
    /// Greets a brand-new person once. The shell remembers the showing before the pop-up opens,
    /// so skipping it, closing DeskAI, or a crash never brings it back.
    /// </summary>
    private async void OnFirstLoaded(object sender, RoutedEventArgs args)
    {
        RootNavigation.Loaded -= OnFirstLoaded;
        if (_shell is not null && await _shell.ClaimFirstWelcomeAsync())
        {
            await ShowWelcomeAsync();
        }
    }

    /// <summary>
    /// Opens the welcome, at the first start or from Privacy and AI. A Connect press closes it and
    /// asks Home's own "Connect your …?" question; only that question's yes connects, and then
    /// Organize opens on the folder, exactly as from Home.
    /// </summary>
    internal async Task ShowWelcomeAsync()
    {
        if (_welcome is null || _welcomeOpen || Content?.XamlRoot is not { } root)
        {
            return;
        }

        _welcomeOpen = true;
        try
        {
            await _welcome.OpenAsync();
            if (!await Views.WelcomeDialog.ShowAsync(root, _welcome)
                || _welcome.ChosenFolder is not { } kind
                || _welcome.Folders.Find(kind) is not { } row)
            {
                return;
            }

            if (!row.IsConnected && !await Views.PersonalFolderDialogs.ConfirmConnectAsync(root, row.Name))
            {
                return;
            }

            if (await _welcome.ConnectChosenAsync() is not null)
            {
                GoTo("organize", fresh: true);
            }
            else if (_welcome.Folders.HasMessage)
            {
                await new ContentDialog
                {
                    XamlRoot = root,
                    Title = $"DeskAI could not connect your {row.Name}",
                    Content = new TextBlock { Text = _welcome.Folders.Message, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 },
                    CloseButtonText = "OK",
                }.ShowAsync();
            }
        }
        finally
        {
            _welcomeOpen = false;
        }
    }

    /// <summary>
    /// Tells the pane's dark-mode switch what the window is actually showing while the saved
    /// choice is "Follow Windows". The view model cannot see WinUI, so the window reports it.
    /// </summary>
    private void ReportTheme() =>
        _shell?.ReportWindowTheme(RootNavigation.ActualTheme == ElementTheme.Dark);

    /// <summary>The dark-mode switch: saves always-dark or always-light for DeskAI's window only.</summary>
    private async void OnDarkModeToggled(object sender, RoutedEventArgs args)
    {
        if (_shell is null || DarkModeSwitch.IsOn == _shell.IsDark)
        {
            // The binding just moved the switch to match the saved choice; nothing to save.
            return;
        }

        await _shell.SetDarkAsync(DarkModeSwitch.IsOn);
    }

    /// <summary>The top-bar search box: opens Search with the phrase already run.</summary>
    private void OnFindSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_shell?.FindFile(args.QueryText) == true)
        {
            sender.Text = string.Empty;
            GoTo("search", fresh: true);
        }
    }

    /// <summary>The AI pill only opens Privacy and AI, where the choice it describes is made.</summary>
    private void OnAiPillClicked(object sender, RoutedEventArgs args) => GoTo("settings", fresh: false);

    /// <summary>
    /// Closing the window means hiding it when DeskAI has been asked to keep checking.
    /// </summary>
    /// <remarks>
    /// The host — and so the check timer — is untouched, which is the whole of the promise: the
    /// same DeskAI is still running. Reopening shows the state it was left in rather than a fresh
    /// start. With the mode off, this does nothing and the close is a real one.
    /// <para>
    /// Reads <see cref="BackgroundPresenceController.KeepsRunningWhenClosed"/> rather than a
    /// field kept here, because this handler cannot await a fresh read and a cached copy on the
    /// window would go stale the moment the switch changed on the Automatic tasks page without
    /// this window ever navigating. When the controller is unavailable, the safe direction is a
    /// real close, never a hidden one — so the check below defaults to false, not true.
    /// </para>
    /// </remarks>
    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_presence is { KeepsRunningWhenClosed: true } && !_quitting)
        {
            args.Cancel = true;
            sender.Hide();
            ShowWhereItWentOnceThisRun();
        }
    }

    /// <summary>
    /// Lets the next close be a real one, because DeskAI is being quit.
    /// </summary>
    /// <remarks>
    /// Without this, "Quit DeskAI" would be unable to finish: shutting the app down closes this
    /// window, <see cref="OnClosing"/> would cancel that close exactly as it does for the close
    /// button, and DeskAI would keep running with no way left to stop it.
    /// </remarks>
    internal void AllowTheRealClose() => _quitting = true;

    /// <summary>
    /// Brings the window back, from the icon near the clock, a notification, or a second launch.
    /// </summary>
    /// <remarks>
    /// Showing is not enough on its own: a window restored by another process's request can come
    /// back behind whatever the person is looking at, which reads as nothing having happened.
    /// Activating and then asking for the foreground is what actually puts it in front of them.
    /// </remarks>
    internal void Reveal()
    {
        AppWindow.Show(activateWindow: true);
        Activate();
        TrayInterop.SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    /// <summary>
    /// Says where DeskAI went, the first time the window is hidden in a run.
    /// </summary>
    /// <remarks>
    /// A window that vanishes while the program keeps running is alarming unless the person is
    /// told, and the icon near the clock is small and easy to miss. Shown through Windows
    /// notifications because there is no longer a window to put a message in; on a machine
    /// where those are unavailable, nothing is shown rather than something invisible being
    /// counted as told.
    /// <para>
    /// Gated on <see cref="BackgroundPresenceController.KeepsRunningWhenClosed"/> too, the same
    /// value <see cref="OnClosing"/> just checked, so this can never claim an icon is near the
    /// clock in the one state where none is being shown.
    /// </para>
    /// </remarks>
    private void ShowWhereItWentOnceThisRun()
    {
        if (_toldThemWhereItWent
            || _notifier is not { IsAvailable: true }
            || _presence is not { KeepsRunningWhenClosed: true }
            // Only checking in the background gets the notice; the owner did not pick a
            // notification for quick search (spec decision 12), and its card says what closing does.
            || _presence.QuickSearchKeepsItRunning)
        {
            return;
        }

        _toldThemWhereItWent = true;
        _notifier.Notify(BackgroundCheckingChoice.WhereItWent.Title, BackgroundCheckingChoice.WhereItWent.Body);
    }

    /// <summary>
    /// Keeps the pane's scope reminder honest.
    /// </summary>
    /// <remarks>
    /// Refreshed on every navigation because connecting or disconnecting a folder happens
    /// on another page, and a stale reminder about what DeskAI can reach is exactly the
    /// thing this label exists to prevent. The keep-running mode used to be read here too, but
    /// a copy refreshed only on navigation went stale the moment the switch changed on the
    /// Automatic tasks page with no navigation in between — see
    /// <see cref="BackgroundPresenceController.KeepsRunningWhenClosed"/>, which <see cref="OnClosing"/>
    /// reads directly instead.
    /// </remarks>
    internal async Task RefreshScopeAsync()
    {
        if (_shell is not null)
        {
            await _shell.RefreshAsync();
            ReportTheme();
        }
    }

    /// <summary>
    /// "Review in Organize": opens Tidy a folder on the folder with the most matches, with its
    /// list ready. It changes nothing on the way; files move only if Tidy is pressed there.
    /// </summary>
    private void OnReviewInOrganizeClicked(object sender, RoutedEventArgs args)
    {
        _shell?.ReviewInOrganize();
        GoTo("organize", fresh: true);
    }

    /// <summary>Takes someone to the page with the practice run and the check history.</summary>
    private void OnSeeAutomaticTasksClicked(object sender, RoutedEventArgs args)
    {
        _shell?.DismissFindingCommand.Execute(null);
        GoTo("automation", fresh: false);
    }

    /// <summary>
    /// Opens a page and selects its menu item, so the side menu never points at a page other
    /// than the one on screen. Also used by My workspace's "Open in Search".
    /// </summary>
    internal void GoTo(string route, bool fresh)
    {
        _navigationService?.Navigate(route, fresh);
        _shell?.ShowPage(route);
        foreach (var item in RootNavigation.MenuItems)
        {
            if (item is NavigationViewItem { Tag: string tag } menuItem && tag == route)
            {
                RootNavigation.SelectedItem = menuItem;
                break;
            }
        }

        _ = RefreshScopeAsync();
    }

    private MainWindow()
    {
        InitializeComponent();
        Title = "DeskAI — startup issue";
        ApplyWindowIcon();
        RootNavigation.IsEnabled = false;
        ContentFrame.Content = new TextBlock
        {
            Margin = new Thickness(32),
            Text = "DeskAI could not initialize its local application state. No personal folders were accessed. Check the debug log and restart the app.",
            TextWrapping = TextWrapping.Wrap,
        };
    }

    public static MainWindow CreateStartupFailureWindow() => new();

    /// <summary>
    /// Applies DeskAI's mark to the live unpackaged window. The project icon alone brands the
    /// executable, but WinUI otherwise leaves the title bar on its generic default icon.
    /// </summary>
    private void ApplyWindowIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "DeskAI.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }
    }

    private void OnNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is string tag)
        {
            _navigationService?.Navigate(tag);
            _shell?.ShowPage(tag);
            _ = RefreshScopeAsync();
        }
    }
}
