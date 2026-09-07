using DeskAI.AI;
using DeskAI.App.Navigation;
using DeskAI.App.Preview;
using DeskAI.App.Services;
using DeskAI.App.ViewModels;
using DeskAI.App.Views;
using DeskAI.Core.Ai;
using DeskAI.Infrastructure.DependencyInjection;
using DeskAI.Infrastructure.Logging;
using DeskAI.Infrastructure.Persistence;
using DeskAI.Safety;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace DeskAI.App;

public partial class App : Application
{
    private readonly IHost _host;
    private Window? _window;

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
                }.Where(path => !string.IsNullOrWhiteSpace(path));

                services.AddDeskAiInfrastructure(options =>
                    options.DatabasePath = Path.Combine(appStateDirectory, "deskai.db"));
                services.AddSingleton<IOrganizationSuggestionProvider, NoAiSuggestionProvider>();
                services.AddSingleton<IPathPolicy>(_ => new WindowsPathPolicy(protectedPaths));
                services.AddSingleton<PlanValidator>();
                services.AddSingleton<DemoOrganizationPlanFactory>();
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<IFolderPickerService, WindowsFolderPickerService>();
                services.AddTransient<ShellViewModel>();
                services.AddTransient<OrganizeViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<DashboardPage>();
                services.AddTransient<OrganizePage>();
                services.AddTransient<SearchPage>();
                services.AddTransient<AutomationPage>();
                services.AddTransient<SettingsPage>();
                services.AddTransient<MainWindow>();
            })
            .Build();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            await _host.StartAsync();
            await _host.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
            _window = _host.Services.GetRequiredService<MainWindow>();
            _window.Activate();
        }
        catch (Exception exception)
        {
            var logger = _host.Services.GetRequiredService<ILogger<App>>();
            LogStartupFailure(logger, exception);
            _window = MainWindow.CreateStartupFailureWindow();
            _window.Activate();
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
