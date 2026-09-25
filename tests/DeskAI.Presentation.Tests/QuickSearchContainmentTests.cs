using System.Reflection;
using DeskAI.App.ViewModels;
using DeskAI.Core.QuickSearch;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Opening files stays in the one place built for it: only the quick search bar holds the
/// launcher, nothing from the AI side does, and the bar holds nothing that changes a permission.
/// </summary>
public sealed class QuickSearchContainmentTests
{
    private static readonly Assembly[] DeskAi =
    [
        typeof(IFileLauncher).Assembly,                                   // Core
        typeof(QuickSearchViewModel).Assembly,                            // Presentation
        typeof(DeskAI.AI.ConfiguredSuggestionProvider).Assembly,          // AI
        typeof(DeskAI.Infrastructure.Launching.WindowsFileLauncher).Assembly,
        typeof(DeskAI.Safety.PlanValidator).Assembly,
    ];

    [Fact]
    public void Only_the_quick_search_bar_holds_the_launcher()
    {
        var holders = DeskAi
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetConstructors().Any(constructor =>
                constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(IFileLauncher))))
            .Select(type => type.Name)
            .ToArray();

        Assert.Equal([nameof(QuickSearchViewModel)], holders);
    }

    [Fact]
    public void Only_the_launcher_holds_the_shell_starter()
    {
        var holders = DeskAi
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetConstructors().Any(constructor =>
                constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(DeskAI.Infrastructure.Launching.IShellStarter))))
            .Select(type => type.Name)
            .ToArray();

        Assert.Equal([nameof(DeskAI.Infrastructure.Launching.WindowsFileLauncher)], holders);
    }

    [Fact]
    public void The_bar_holds_nothing_that_changes_a_permission_or_talks_to_AI()
    {
        var parameters = typeof(QuickSearchViewModel).GetConstructors().Single().GetParameters().Select(p => p.ParameterType);

        Assert.DoesNotContain(parameters, type =>
            type.Name.Contains("ConnectedFolder", StringComparison.Ordinal) || type.Name.Contains("Permission", StringComparison.Ordinal)
            || type.Name.Contains("Provider", StringComparison.Ordinal) || type.Name.Contains("Ai", StringComparison.Ordinal)
            || type.Name.Contains("Executor", StringComparison.Ordinal) || type.Name.Contains("Repository", StringComparison.Ordinal));
    }
}
