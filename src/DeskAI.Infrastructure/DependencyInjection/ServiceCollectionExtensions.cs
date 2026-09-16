using DeskAI.Core.Abstractions;
using DeskAI.Core.Classification;
using DeskAI.Core.Recipes;
using DeskAI.Core.Plans;
using DeskAI.Infrastructure.Persistence;
using DeskAI.Infrastructure.Execution;
using DeskAI.Infrastructure.Indexing;
using DeskAI.Infrastructure.Scanning;
using DeskAI.Infrastructure.Security;
using DeskAI.Infrastructure.Time;
using Microsoft.Extensions.DependencyInjection;

namespace DeskAI.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDeskAiInfrastructure(
        this IServiceCollection services,
        Action<DatabaseOptions> configureDatabase)
    {
        services.AddOptions<DatabaseOptions>().Configure(configureDatabase);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICredentialVault>(_ => OperatingSystem.IsWindows()
            ? new WindowsCredentialVault()
            : new UnsupportedCredentialVault());
        services.AddSingleton(DefaultFileTypeRules.Create());
        services.AddSingleton<IFileClassifier, DeterministicFileClassifier>();
        services.AddSingleton<IOrganizationPlanner, OrganizationPlanner>();
        services.AddSingleton(DefaultFolderRecipe.Create());
        services.AddSingleton<IFileScanner, WindowsMetadataScanner>();
        services.AddSingleton<IReadOnlyFolderService, ReadOnlyFolderService>();
        // Names and kinds directly inside a connected folder, for folder templates. No contents.
        services.AddSingleton<IFolderNameLookup, FolderNameLookup>();
        services.AddSingleton<IFileIndex, SqliteFileIndex>();
        services.AddSingleton<IMetadataIndexService, MetadataIndexService>();
        services.AddSingleton<IDatabaseInitializer, SqliteDatabaseInitializer>();
        services.AddSingleton<IAuthorizedRootRepository, SqliteAuthorizedRootRepository>();
        services.AddSingleton<ISavedSearchRepository, SqliteSavedSearchRepository>();
        services.AddSingleton<IRuleRepository, SqliteRuleRepository>();
        services.AddSingleton<IAutomaticCheckSettingsRepository, SqliteAutomaticCheckSettingsRepository>();
        services.AddSingleton<IAutomaticCheckHistoryRepository, SqliteAutomaticCheckHistoryRepository>();
        services.AddSingleton<IAiSettingsRepository, SqliteAiSettingsRepository>();
        services.AddSingleton<IAppearanceSettingsRepository, SqliteAppearanceSettingsRepository>();
        services.AddSingleton<IAiUsageBudget, SqliteAiUsageBudget>();
        services.AddSingleton<IPlanRepository, SqlitePlanRepository>();
        services.AddSingleton<IOperationJournal, SqliteOperationJournal>();
        // The only code in DeskAI that moves a file. It acts only in a folder someone connected
        // and allowed to be tidied, checking again before every file. See ADR 0021 and 0022.
        // (The practice executor it once shared its rules with was retired on 2026-09-11.)
        services.AddSingleton<IFolderTidyExecutor, FolderTidyExecutor>();
        return services;
    }
}
