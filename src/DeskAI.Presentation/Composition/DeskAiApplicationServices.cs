using DeskAI.AI;
using DeskAI.AI.Transport;
using DeskAI.App.Services;
using DeskAI.App.ViewModels;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;
using DeskAI.Core.Backup;
using DeskAI.Core.Desktop;
using DeskAI.Core.QuickSearch;
using DeskAI.Core.Rules;
using DeskAI.Core.Search;
using DeskAI.Core.Studio;
using DeskAI.Core.Templates;
using DeskAI.Core.Tidy;
using DeskAI.Core.Welcome;
using DeskAI.Core.Workspace;
using DeskAI.Infrastructure.Content;
using DeskAI.Infrastructure.DependencyInjection;
using DeskAI.Infrastructure.Execution;
using DeskAI.Infrastructure.Launching;
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
        IEnumerable<string> protectedPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(protectedPaths);
        var protectedList = protectedPaths.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();

        services.AddDeskAiInfrastructure(options => options.DatabasePath = databasePath);
        services.AddSingleton<IAiHttpTransport>(_ => new HttpClientAiTransport(
            new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                Timeout = Timeout.InfiniteTimeSpan,
            }));
        services.AddSingleton<IOrganizationSuggestionProvider, ConfiguredSuggestionProvider>();
        // "Check this now" on Privacy and AI: the one AI path that carries no file information
        // at all, so a person can see their choice works before trusting it with anything.
        services.AddSingleton<IAiConnectionCheck, ConfiguredAiConnectionCheck>();
        services.AddSingleton<IPathPolicy>(_ => new WindowsPathPolicy(protectedList));
        services.AddSingleton<PlanValidator>();
        services.AddSingleton<IPlanSafetyCheck, PlanSafetyCheck>();
        services.AddSingleton<TidyPermissionService>();
        services.AddSingleton<TidySuggestionService>();
        // Talks to AI about your own folders. It holds nothing that can look in a folder or
        // change a file, and it sends only after the page has shown what would be sent.
        services.AddSingleton<TidyAiService>();
        // Reads a typed sentence through AI (V1.1, ADR 0033). It holds the settings, the AI
        // connection, and the clock, and nothing that can see a file; a test asserts it.
        services.AddSingleton<SentenceAiService>();
        // Ask DeskAI on Home (ADR 0035): sends a question through the sentence service and answers
        // it from the local index. It holds nothing that can open or change a file; a test asserts it.
        services.AddSingleton<AskDeskAiService>();
        // Find groups on Desktop Studio (ADR 0042): looks at the Desktop read-only and holds the AI
        // connection, but nothing that can move, rename, or change a file or a Windows setting.
        services.AddSingleton<DesktopLookService>();
        services.AddSingleton<LocalDesktopGrouper>();
        services.AddSingleton<DesktopGroupingService>();
        services.AddSingleton<DesktopInventoryService>();
        services.AddSingleton<DesktopMoveService>();
        services.AddSingleton<TidyRunService>();
        // Tidy while I'm away (V0.9, ADR 0031): the one type reachable from an automatic check that
        // can move a file, under a standing approval with a hard ceiling. A test names it as such.
        services.AddSingleton<AwayTidyService>();
        services.AddSingleton<IAwayTidyRunner>(provider => provider.GetRequiredService<AwayTidyService>());
        services.AddSingleton<FileSearchService>();
        services.AddSingleton(SearchScanBounds.Default);
        services.AddSingleton<ConnectedFolderService>();
        // The only service that opens a file. It refuses any folder that was not
        // connected for reading inside, so registering it grants nothing on its own.
        services.AddSingleton<PdfProcessReader>();
        services.AddSingleton<IContentTextExtractor, PlainTextExtractor>();
        services.AddSingleton<IPdfOcrReader, NoPdfOcrReader>();
        services.AddSingleton<ContentSearchService>();
        // Quick search (ADR 0047): the two read-only searches above, and nothing that can open,
        // change, or send a file. A test asserts it.
        services.AddSingleton<QuickSearchService>();
        // DeskAI's only "open a file" action (ADR 0047). It starts nothing here: the shared
        // registration's starter refuses, and only the app registers the real one.
        services.AddSingleton<IShellStarter, NoShellStarter>();
        services.AddSingleton<IFileLauncher, WindowsFileLauncher>();
        services.AddSingleton<IVisualAssetReader, VisualAssetReader>();
        services.AddSingleton<IVisualImageMatcher, ConfiguredVisualImageMatcher>();
        services.AddSingleton<VisualSearchService>();
        services.AddSingleton<StorageSummaryService>();
        services.AddSingleton<DuplicateFinderService>();
        // Reads whole files to tell real copies apart. Only DuplicateCheckService takes it, and
        // only after the person has seen exactly which files it would read; a test asserts no
        // other service can reach it.
        services.AddSingleton<IFileFingerprinter, FileFingerprinter>();
        services.AddSingleton<DuplicateCheckService>();
        services.AddSingleton<RuleSimulationService>();
        services.AddSingleton<AutomaticCheckService>();
        services.AddSingleton<AutomaticCheckCoordinator>();
        // The only background work DeskAI does. It runs while the app runs and stops when
        // it stops; nothing is registered with Windows to start it again. See ADR 0017.
        services.AddHostedService<AutomaticCheckTimer>();
        // Which folder Organize opens on after "Review in Organize". A folder ID, nothing more.
        services.AddSingleton<OrganizeRequest>();
        // My workspace. Both create or read saved searches and rules only; neither can reach a
        // file, and tests fail if either is given anything that can.
        services.AddSingleton<StarterPackService>();
        services.AddSingleton<PinnedSearchService>();
        // Folder templates. The one My workspace service that can change a folder: it makes
        // empty folders through the same executor Tidy uses, with the tidy permission, and
        // nothing else. See ADR 0027.
        services.AddSingleton<FolderTemplateService>();
        // The wallpaper: DeskAI's one change to a Windows setting, only from the page's button.
        // It holds the setter, the picture inspector, and the settings store, and nothing else.
        services.AddSingleton<WallpaperService>();
        // Which saved search Search runs after "Open in Search". A saved-search ID, nothing more.
        services.AddSingleton<SearchRequest>();
        // Back up and restore, and Start fresh (V0.8). Neither holds anything that can reach a
        // file on disk beyond the one backup file the person chose; a test asserts it.
        services.AddSingleton<BackupService>();
        services.AddSingleton<FreshStartService>();
        // The first-run welcome (2026-09-24): one remembered value and the folder list; nothing
        // that can reach a file.
        services.AddSingleton<WelcomeService>();
        // Quick search's three remembered values (ADR 0047). The settings store and nothing else.
        services.AddSingleton<QuickSearchSettingsService>();
        // A DeskAI with no notification area is a legitimate DeskAI: it simply never offers
        // to keep running with no window. The Windows one is registered by the app.
        services.AddSingleton<IBackgroundPresence, NoBackgroundPresence>();
        // Paints DeskAI's own window in the chosen look. The Windows one is registered by the app.
        services.AddSingleton<IAppearanceApplier, NoAppearanceApplier>();
        services.AddSingleton<BackgroundPresenceController>();
        // The quick search shortcut. None here; the Windows one is registered by the app.
        services.AddSingleton<IQuickSearchHotKey, NoQuickSearchHotKey>();
        services.AddSingleton<QuickSearchSwitch>();
        services.AddTransient<QuickSearchCardViewModel>();
        services.AddTransient<ShellViewModel>();
        services.AddTransient<TidyViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<AutomationViewModel>();
        services.AddTransient<WorkspaceViewModel>();
        // The "Your folders" card on Home and My workspace. It connects through the same service
        // the folder picker uses and grants nothing beyond that.
        services.AddTransient<PersonalFoldersViewModel>();
        // The first-run welcome's pages. It connects only through the card above, after Home's question.
        services.AddTransient<WelcomeViewModel>();
        services.AddTransient<AskDeskAiViewModel>();
        services.AddTransient<DesktopStudioViewModel>();
        services.AddSingleton(QuickSearchTiming.Default);
        // The quick search bar (ADR 0047): the one holder of the file launcher. One bar per DeskAI.
        services.AddSingleton<QuickSearchViewModel>();
        return services;
    }
}
