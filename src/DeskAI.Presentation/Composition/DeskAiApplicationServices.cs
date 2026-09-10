using DeskAI.AI;
using DeskAI.AI.Transport;
using DeskAI.App.Preview;
using DeskAI.App.ViewModels;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;
using DeskAI.Core.Rules;
using DeskAI.Core.Search;
using DeskAI.Infrastructure.Content;
using DeskAI.Infrastructure.DependencyInjection;
using DeskAI.Infrastructure.Execution;
using DeskAI.Infrastructure.Time;
using DeskAI.Safety;
using Microsoft.Extensions.DependencyInjection;

namespace DeskAI.App.Composition;

/// <summary>
/// Everything DeskAI is made of, except the window and the Windows dialogs.
/// </summary>
/// <remarks>
/// The running app and the page tests both call this, so a test builds DeskAI the same way
/// the app does. A service the app forgot to register, or registered wrongly, fails a test
/// instead of failing on someone's screen. Tests then replace only what must never be real
/// in a test: the credential store, the internet, and Windows notifications.
/// </remarks>
public static class DeskAiApplicationServices
{
    public static IServiceCollection AddDeskAiApplication(
        this IServiceCollection services,
        string databasePath,
        IEnumerable<string> protectedPaths,
        Action<DemoWorkspaceOptions>? configureDemoWorkspace = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(protectedPaths);
        var protectedList = protectedPaths.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();

        services.AddDeskAiInfrastructure(
            options => options.DatabasePath = databasePath,
            configureDemoWorkspace);
        services.AddSingleton<IAiHttpTransport>(_ => new HttpClientAiTransport(
            new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                Timeout = Timeout.InfiniteTimeSpan,
            }));
        services.AddSingleton<IOrganizationSuggestionProvider, ConfiguredSuggestionProvider>();
        services.AddSingleton<IPathPolicy>(_ => new WindowsPathPolicy(protectedList));
        services.AddSingleton<PlanValidator>();
        services.AddSingleton<DemoOrganizationPlanFactory>();
        services.AddSingleton<FileSearchService>();
        services.AddSingleton<ConnectedFolderService>();
        // The only service that opens a file. It refuses any folder that was not
        // connected for reading inside, so registering it grants nothing on its own.
        services.AddSingleton<IContentTextExtractor, PlainTextExtractor>();
        services.AddSingleton<ContentSearchService>();
        services.AddSingleton<StorageSummaryService>();
        services.AddSingleton<DuplicateFinderService>();
        services.AddSingleton<RuleSimulationService>();
        services.AddSingleton<AutomaticCheckService>();
        services.AddSingleton<AutomaticCheckCoordinator>();
        // The only background work DeskAI does. It runs while the app runs and stops when
        // it stops; nothing is registered with Windows to start it again. See ADR 0017.
        services.AddHostedService<AutomaticCheckTimer>();
        services.AddTransient<ShellViewModel>();
        services.AddTransient<OrganizeViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<AutomationViewModel>();
        return services;
    }
}
