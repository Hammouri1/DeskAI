using DeskAI.Core.Abstractions;
using DeskAI.Core.Indexing;
using DeskAI.Core.Roots;
using DeskAI.Core.Search;

namespace DeskAI.Core.Rules;

/// <summary>What a set of rules would do inside one connected folder.</summary>
public sealed record FolderRuleSimulation(Guid RootId, string RootName, RuleRunPreview Preview);

/// <summary>What the rules would do across every connected folder.</summary>
public sealed record RuleSimulation(
    IReadOnlyList<FolderRuleSimulation> Folders,
    int RulesConsidered,
    int RulesTurnedOff)
{
    public static RuleSimulation Empty { get; } = new([], 0, 0);

    public int ProposalCount => Folders.Sum(folder => folder.Preview.Proposals.Count);

    public int ConflictCount => Folders.Sum(folder => folder.Preview.Conflicts.Count);
}

/// <summary>
/// Shows what the stored rules would do, without doing any of it.
/// </summary>
/// <remarks>
/// <para>
/// This is a practice run and only ever a practice run. It reads remembered metadata,
/// evaluates rules in memory, and returns a description. There is no path from here to a
/// file being moved: it produces no plan, holds no executor, and touches no file.
/// </para>
/// <para>
/// Each connected folder is simulated separately, because a rule's destination is a folder
/// inside the connected folder. Two people's "Documents" are different places, and merging
/// the results would suggest otherwise.
/// </para>
/// <para>
/// Scope comes from <see cref="FileSearchService.IsSearchable"/>, the same predicate search
/// and the storage summary use, so rules can never describe a folder search would not look
/// in. Rules need names, sizes, and dates only — nothing here reads inside a file, whatever
/// permission a folder was given.
/// </para>
/// </remarks>
public sealed class RuleSimulationService(
    IRuleRepository rules,
    IAuthorizedRootRepository roots,
    IFileIndex index)
{
    /// <summary>How many remembered files one folder contributes to a practice run.</summary>
    private const int MaxFilesPerFolder = SearchQuery.MaxLimit;

    private readonly IRuleRepository _rules = rules;
    private readonly IAuthorizedRootRepository _roots = roots;
    private readonly IFileIndex _index = index;

    public async Task<RuleSimulation> SimulateAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var stored = await _rules.ListAsync(cancellationToken).ConfigureAwait(false);
        var enabled = stored.Where(rule => rule.IsEnabled).ToArray();
        var turnedOff = stored.Count - enabled.Length;

        if (enabled.Length == 0)
        {
            return RuleSimulation.Empty with { RulesConsidered = 0, RulesTurnedOff = turnedOff };
        }

        var folders = (await _roots.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(FileSearchService.IsSearchable)
            .ToArray();

        var simulations = new List<FolderRuleSimulation>(folders.Length);
        foreach (var root in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var files = await _index
                .SearchRootAsync(root.Id, new SearchQuery(limit: MaxFilesPerFolder), cancellationToken)
                .ConfigureAwait(false);

            var subjects = files.Select(Describe).ToArray();
            simulations.Add(new FolderRuleSimulation(
                root.Id,
                root.DisplayName,
                RuleSetEvaluator.Evaluate(enabled, subjects, nowUtc)));
        }

        return new RuleSimulation(simulations.AsReadOnly(), enabled.Length, turnedOff);
    }

    /// <summary>Describes a remembered file in the only terms rules may test.</summary>
    private static RuleSubject Describe(IndexedFile file) => new(
        file.RelativePath,
        file.Category,
        file.Kind,
        file.SizeBytes,
        file.ModifiedAtUtc);
}
