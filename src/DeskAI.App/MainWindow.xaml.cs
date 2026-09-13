using DeskAI.App.Navigation;
using DeskAI.App.Services;
using DeskAI.App.ViewModels;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Rules;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskAI.App;

public sealed partial class MainWindow : Window
{
    private readonly INavigationService? _navigationService;
    private readonly ShellViewModel? _shell;
    private readonly IAutomaticCheckSettingsRepository? _settings;
    private readonly IFindingNotifier? _notifier;

    /// <summary>What the stored mode said the last time it was read. See <see cref="OnClosing"/>.</summary>
    private bool _keepsRunningWhenClosed;

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
        IAutomaticCheckSettingsRepository settings,
        IFindingNotifier notifier)
    {
        InitializeComponent();
        Title = "DeskAI";
        _shell = viewModel;
        RootNavigation.DataContext = viewModel;
        _navigationService = navigationService;
        _settings = settings;
        _notifier = notifier;
        _navigationService.Initialize(ContentFrame);
        RootNavigation.SelectedItem = RootNavigation.MenuItems[0];
        _navigationService.Navigate("dashboard");
        AppWindow.Closing += OnClosing;
        _ = RefreshScopeAsync();
    }

    /// <summary>
    /// Closing the window means hiding it when DeskAI has been asked to keep checking.
    /// </summary>
    /// <remarks>
    /// The host — and so the check timer — is untouched, which is the whole of the promise: the
    /// same DeskAI is still running. Reopening shows the state it was left in rather than a fresh
    /// start. With the mode off, this does nothing and the close is a real one.
    /// </remarks>
    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_keepsRunningWhenClosed && !_quitting)
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
    /// </remarks>
    private void ShowWhereItWentOnceThisRun()
    {
        if (_toldThemWhereItWent || _notifier is not { IsAvailable: true })
        {
            return;
        }

        _toldThemWhereItWent = true;
        _notifier.Notify(
            "DeskAI is still running.",
            "You'll find it near the clock. Right-click it to open DeskAI or quit.");
    }

    /// <summary>
    /// Keeps the pane's scope reminder honest, and the close behaviour with it.
    /// </summary>
    /// <remarks>
    /// Refreshed on every navigation because connecting or disconnecting a folder happens
    /// on another page, and a stale reminder about what DeskAI can reach is exactly the
    /// thing this label exists to prevent. The keep-running mode is read here for the same
    /// reason: it is switched on the Automatic tasks page, and a stale copy would either hide
    /// the window when someone expected it to close or close it when they expected it to stay.
    /// </remarks>
    private async Task RefreshScopeAsync()
    {
        if (_shell is not null)
        {
            await _shell.RefreshAsync();
        }

        if (_settings is null)
        {
            return;
        }

        try
        {
            var stored = await _settings.LoadAsync();
            _keepsRunningWhenClosed = stored.Mode == AutomaticCheckMode.InBackground;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.Data.Common.DbException
            or IOException
            or OperationCanceledException)
        {
            // The narrow answer is the safe one: an unreadable setting must not be treated as
            // permission to keep running with no window.
            _keepsRunningWhenClosed = false;
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

    private void GoTo(string route, bool fresh)
    {
        _navigationService?.Navigate(route, fresh);
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
        RootNavigation.IsEnabled = false;
        ContentFrame.Content = new TextBlock
        {
            Margin = new Thickness(32),
            Text = "DeskAI could not initialize its local application state. No personal folders were accessed. Check the debug log and restart the app.",
            TextWrapping = TextWrapping.Wrap,
        };
    }

    public static MainWindow CreateStartupFailureWindow() => new();

    private void OnNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is string tag)
        {
            _navigationService?.Navigate(tag);
            _ = RefreshScopeAsync();
        }
    }
}
