using DeskAI.Core.Abstractions;
using DeskAI.Core.Search;

namespace DeskAI.Core.Workspace;

public enum StarterPackItemKind
{
    Search,
    Rule,
}

/// <summary>One thing a pack would add, and why it will not be added if it will not.</summary>
public sealed record StarterPackItem(StarterPackItemKind Kind, string Name, string Description, string? SkipReason)
{
    public bool WillBeAdded => SkipReason is null;
}

/// <summary>Exactly what adding a pack would do right now, before anything is saved.</summary>
public sealed record StarterPackPreview(StarterPack Pack, IReadOnlyList<StarterPackItem> Items)
{
    public bool AddsAnything => Items.Any(item => item.WillBeAdded);
}

/// <summary>What adding a pack actually did.</summary>
/// <param name="StoppedBecause">
/// Set when something failed part-way. Whatever was added before that stays added and is
/// listed, because each item is an ordinary search or switched-off rule the person can remove.
/// </param>
public sealed record StarterPackOutcome(
    StarterPack Pack,
    IReadOnlyList<string> AddedSearches,
    IReadOnlyList<string> AddedRules,
    IReadOnlyList<StarterPackItem> Skipped,
    IReadOnlyList<string> Pinned,
    IReadOnlyList<string> AddedButNotPinned,
    string? StoppedBecause);

/// <summary>
/// Previews and adds starter packs.
/// </summary>
/// <remarks>
/// <para>
/// It can create saved searches and rules and nothing else. It holds no executor, planner,
/// scanner, reader, journal, credential, or AI, and a test fails if one is added.
/// </para>
/// <para>
/// Nothing a person already has is ever replaced. A name already in use, whatever its
/// capitals, is skipped and reported. Rules are always saved switched off, by
/// <see cref="StarterPackRule.ToRule"/>, so adding a pack can never change what DeskAI suggests
/// moving until the person turns a rule on.
/// </para>
/// </remarks>
public sealed class StarterPackService(ISavedSearchRepository searches, IRuleRepository rules, IClock clock)
{
    private readonly ISavedSearchRepository _searches = searches;
    private readonly IRuleRepository _rules = rules;
    private readonly IClock _clock = clock;

    /// <summary>Works out what adding the pack would do. Saves nothing.</summary>
    public async Task<StarterPackPreview> PreviewAsync(string packId, CancellationToken cancellationToken = default)
    {
        var pack = FindPack(packId);
        var (items, _) = await PlanAsync(pack, cancellationToken).ConfigureAwait(false);
        return new StarterPackPreview(pack, items);
    }

    /// <summary>
    /// Adds what the pack offers, working it out again from what is stored now.
    /// </summary>
    /// <remarks>
    /// The preview a person saw may be minutes old; a search made in the meantime must be
    /// skipped rather than overwritten, so the plan is rebuilt here instead of trusted.
    /// </remarks>
    public async Task<StarterPackOutcome> AddAsync(string packId, CancellationToken cancellationToken = default)
    {
        var pack = FindPack(packId);
        var (items, pinnedAlready) = await PlanAsync(pack, cancellationToken).ConfigureAwait(false);

        var addedSearches = new List<string>();
        var addedRules = new List<string>();
        var pinned = new List<string>();
        var notPinned = new List<string>();
        string? stoppedBecause = null;

        try
        {
            foreach (var search in pack.Searches)
            {
                if (!WillAdd(items, StarterPackItemKind.Search, search.Name))
                {
                    continue;
                }

                var pin = pinnedAlready + pinned.Count < SavedSearch.MaxPinned;
                await _searches.SaveAsync(
                    SavedSearch.Create(Guid.NewGuid(), search.Name, search.Phrase, _clock.UtcNow, pin),
                    cancellationToken).ConfigureAwait(false);
                addedSearches.Add(search.Name);
                (pin ? pinned : notPinned).Add(search.Name);
            }

            foreach (var rule in pack.Rules)
            {
                if (!WillAdd(items, StarterPackItemKind.Rule, rule.Name))
                {
                    continue;
                }

                await _rules.SaveAsync(rule.ToRule(Guid.NewGuid()), cancellationToken).ConfigureAwait(false);
                addedRules.Add(rule.Name);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.Data.Common.DbException
            or IOException)
        {
            stoppedBecause = exception.Message;
        }

        return new StarterPackOutcome(
            pack,
            addedSearches.AsReadOnly(),
            addedRules.AsReadOnly(),
            items.Where(item => !item.WillBeAdded).ToArray(),
            pinned.AsReadOnly(),
            notPinned.AsReadOnly(),
            stoppedBecause);
    }

    private static StarterPack FindPack(string packId) =>
        StarterPackCatalog.Find(packId)
        ?? throw new ArgumentException($"There is no starter pack called \"{packId}\".", nameof(packId));

    private static bool WillAdd(IReadOnlyList<StarterPackItem> items, StarterPackItemKind kind, string name) =>
        items.Any(item => item.Kind == kind && item.WillBeAdded
            && string.Equals(item.Name, name, StringComparison.Ordinal));

    private async Task<(IReadOnlyList<StarterPackItem> Items, int PinnedAlready)> PlanAsync(
        StarterPack pack,
        CancellationToken cancellationToken)
    {
        var existingSearches = await _searches.ListAsync(cancellationToken).ConfigureAwait(false);
        var existingRules = await _rules.ListAsync(cancellationToken).ConfigureAwait(false);
        var searchNames = existingSearches.Select(search => search.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ruleNames = existingRules.Select(rule => rule.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var items = new List<StarterPackItem>();
        var searchCount = existingSearches.Count;
        foreach (var search in pack.Searches)
        {
            string? skip = null;
            if (searchNames.Contains(search.Name))
            {
                skip = $"You already have a search called {search.Name}.";
            }
            else if (searchCount >= SavedSearch.MaxSavedSearches)
            {
                skip = $"You already have {SavedSearch.MaxSavedSearches} saved searches.";
            }
            else
            {
                searchCount++;
            }

            items.Add(new StarterPackItem(StarterPackItemKind.Search, search.Name, search.Phrase, skip));
        }

        foreach (var rule in pack.Rules)
        {
            var skip = ruleNames.Contains(rule.Name) ? $"You already have a rule called {rule.Name}." : null;
            items.Add(new StarterPackItem(
                StarterPackItemKind.Rule,
                rule.Name,
                rule.ToRule(Guid.NewGuid()).Describe(),
                skip));
        }

        return (items.AsReadOnly(), existingSearches.Count(search => search.IsPinned));
    }
}
