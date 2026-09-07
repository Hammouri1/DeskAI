using DeskAI.Core.Abstractions;
using DeskAI.Core.Classification;
using DeskAI.Core.Recipes;
using DeskAI.Core.Plans;
using DeskAI.Infrastructure.Persistence;
using DeskAI.Infrastructure.Execution;
using DeskAI.Infrastructure.Scanning;
using DeskAI.Infrastructure.Time;
using Microsoft.Extensions.DependencyInjection;

namespace DeskAI.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDeskAiInfrastructure(
        this IServiceCollection services,
        Action<DatabaseOptions> configureDatabase,
        Action<DemoWorkspaceOptions>? configureDemoWorkspace = null)
    {
        services.AddOptions<DatabaseOptions>().Configure(configureDatabase);
        services.AddOptions<DemoWorkspaceOptions>().Configure(options =>
            options.BasePath = Path.Combine(Path.GetTempPath(), "DeskAI-Demos"));
        if (configureDemoWorkspace is not null)
        {
            services.Configure(configureDemoWorkspace);
        }
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton(DefaultFileTypeRules.Create());
        services.AddSingleton<IFileClassifier, DeterministicFileClassifier>();
        services.AddSingleton<IOrganizationPlanner, OrganizationPlanner>();
        services.AddSingleton(DefaultFolderRecipe.Create());
        services.AddSingleton<IFileScanner, WindowsMetadataScanner>();
        services.AddSingleton<IDatabaseInitializer, SqliteDatabaseInitializer>();
        services.AddSingleton<IAuthorizedRootRepository, SqliteAuthorizedRootRepository>();
        services.AddSingleton<IPlanRepository, SqlitePlanRepository>();
        services.AddSingleton<IOperationJournal, SqliteOperationJournal>();
        services.AddSingleton<TemporaryDemoPlanExecutor>();
        services.AddSingleton<IPlanExecutor>(provider => provider.GetRequiredService<TemporaryDemoPlanExecutor>());
        services.AddSingleton<IUndoService>(provider => provider.GetRequiredService<TemporaryDemoPlanExecutor>());
        return services;
    }
}
