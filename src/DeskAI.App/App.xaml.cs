using DeskAI.App.Composition;
using DeskAI.App.Navigation;
using DeskAI.App.Services;
using DeskAI.App.Views;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Rules;
using DeskAI.Infrastructure.Logging;
using DeskAI.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;

namespace DeskAI.App;

public partial class App : Application
{
    private readonly IHost _host;
    private Window? _window;
    private SingleInstance? _instance;

    /// <summary>
    /// Set the first time "Quit DeskAI" is chosen, so a second click is ignored.
    /// </summary>
    /// <remarks>
    /// The menu is still there while the host is stopping, and two clicks would mean two
    /// <c>StopAsync</c> calls and two exits. Only ever read and written on the UI thread.
    /// </remarks>
    private bool _quitInProgress;

    internal Window? MainAppWindow => _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;

        var appStateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeskAI");

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(new RedactingDebugLoggerProvider());
            })
            .ConfigureServices(services =>
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var protectedPaths = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    Environment.SystemDirectory,
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    appStateDirectory,
                    AppContext.BaseDirectory,
                    Path.Combine(userProfile, ".ssh"),
                    Path.Combine(userProfile, ".aws"),
                    Path.Combine(userProfile, ".azure"),
                    Path.Combine(userProfile, ".kube"),
                    Path.Combine(roamingAppData, "gnupg"),
                    Path.Combine(roamingAppData, "Microsoft", "Credentials"),
                    Path.Combine(localAppData, "Microsoft", "Credentials"),
                    Path.Combine(localAppData, "Google", "Chrome", "User Data"),
                    Path.Combine(localAppData, "Microsoft", "Edge", "User Data"),
                };

                // Everything except the window lives in one shared registration, which the
                // page tests also use. Only the Windows-facing pieces are added here.
                services.AddDeskAiApplication(Path.Combine(appStateDirectory, "deskai.db"), protectedPaths);
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<IFolderPickerService, WindowsFolderPickerService>();
                services.AddSingleton<IFindingNotifier, WindowsFindingNotifier>();

                // Replace, never add alongside. A second registration would leave
                // NoBackgroundPresence reachable through IEnumerable<IBackgroundPresence>, and
                // whichever one was resolved would decide whether DeskAI offers to keep running
                // with no window — a promise that must not depend on resolution order. Same rule,
                // and the same reason, as TestApp.Replace.
                foreach (var existing in services
                    .Where(descriptor => descriptor.ServiceType == typeof(IBackgroundPresence))
                    .ToArray())
                {
                    services.Remove(existing);
                }

                services.AddSingleton<IBackgroundPresence, TrayPresence>();
                services.AddTransient<DashboardPage>();
                services.AddTransient<OrganizePage>();
                services.AddTransient<SearchPage>();
                services.AddTransient<AutomationPage>();
                services.AddTransient<WorkspacePage>();
                services.AddTransient<SettingsPage>();
                services.AddTransient<MainWindow>();
            })
            .Build();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _instance = SingleInstance.Acquire();
            if (SingleInstanceDecision.Decide(_instance.AnotherIsAlreadyRunning) == LaunchAction.RevealTheRunningOneAndExit)
            {
                // Never a second DeskAI: two SQLite writers against one database, and two timers
                // producing two counts for one state. Never a silent nothing either — launching
                // it again is what a person does when they want the window back.
                _instance.RevealTheRunningOne();
                Exit();
                return;
            }

            await _host.StartAsync();
            await _host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
            var window = _host.Services.GetRequiredService<MainWindow>();
            _window = window;
            window.Activate();
            await ConnectTheBackgroundPresenceAsync(window);
        }
        catch (Exception exception)
        {
            var logger = _host.Services.GetRequiredService<ILogger<App>>();
            LogStartupFailure(logger, exception);
            _window = MainWindow.CreateStartupFailureWindow();
            _window.Activate();
        }
    }

    /// <summary>
    /// Joins the window to the icon near the clock, and to notifications.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The controller is resolved here rather than left to the first page that asks for it. It is
    /// the singleton that owns the icon's pause command, and until something constructs it that
    /// command has no subscriber — so a DeskAI launched and closed without ever visiting the
    /// Automatic tasks page would show a menu whose middle item did nothing.
    /// </para>
    /// <para>
    /// Refreshing once here is what makes a DeskAI that was already set to keep running show its
    /// icon at startup, instead of only after someone opened the page that mentions it.
    /// </para>
    /// </remarks>
    private async Task ConnectTheBackgroundPresenceAsync(MainWindow window)
    {
        var presence = _host.Services.GetRequiredService<IBackgroundPresence>();
        presence.OpenRequested += (_, _) => Reveal(window);
        presence.QuitRequested += (_, _) => _ = QuitAsync(presence, window);

        // After the handlers, never before: this creates the window a second launch posts to, and a
        // message that arrived first would find nothing listening. See EnsureMessageWindow for why
        // the window cannot wait for the icon, and why this call belongs here on the UI thread.
        if (presence is TrayPresence tray)
        {
            tray.EnsureMessageWindow();
        }

        // A notification about something found is useless if clicking it leads nowhere, and with
        // the window hidden that is exactly where it would lead.
        try
        {
            AppNotificationManager.Default.NotificationInvoked += (_, _) => Reveal(window);
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // Notifications are unavailable on this machine; WindowsFindingNotifier has already
            // recorded that, and there will be no notification to click.
        }

        var controller = _host.Services.GetRequiredService<BackgroundPresenceController>();
        controller.Refresh(await _host.Services.GetRequiredService<IAutomaticCheckSettingsRepository>().LoadAsync());
    }

    /// <summary>
    /// Shows the window again, on the UI thread whatever thread asked.
    /// </summary>
    /// <remarks>
    /// A notification click and a posted message from a second launch both arrive from outside
    /// the UI thread's own work, and touching a window from another thread is not allowed.
    /// </remarks>
    private static void Reveal(MainWindow window)
    {
        if (!window.DispatcherQueue.TryEnqueue(window.Reveal))
        {
            // The queue is shutting down, which means DeskAI is closing anyway.
        }
    }

    /// <summary>
    /// "Quit DeskAI", from the icon near the clock. The only way to stop a DeskAI with no window.
    /// </summary>
    /// <remarks>
    /// The icon goes first, so nothing is left near the clock claiming DeskAI is still checking
    /// while it is stopping. Stopping the host is what ends the check timer; nothing is left
    /// running and nothing is registered with Windows to start it again.
    /// </remarks>
    private async Task QuitAsync(IBackgroundPresence presence, MainWindow window)
    {
        if (_quitInProgress)
        {
            return;
        }

        _quitInProgress = true;

        try
        {
            window.AllowTheRealClose();
            presence.Hide();
            await _host.StopAsync();
        }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException)
        {
            var logger = _host.Services.GetRequiredService<ILogger<App>>();
            LogUnhandledUiError(logger, exception);
        }
        finally
        {
            _instance?.Dispose();

            // Back onto the UI thread: the await above can resume anywhere, and Exit closes
            // windows, which only their own thread may do.
            window.DispatcherQueue.TryEnqueue(Exit);
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        var logger = _host.Services.GetRequiredService<ILogger<App>>();
        LogUnhandledUiError(logger, args.Exception);
        args.Handled = true;
    }

    [LoggerMessage(EventId = 2001, Level = LogLevel.Critical, Message = "DeskAI could not finish safe startup.")]
    private static partial void LogStartupFailure(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Error, Message = "An unexpected UI error was contained.")]
    private static partial void LogUnhandledUiError(ILogger logger, Exception exception);
}
