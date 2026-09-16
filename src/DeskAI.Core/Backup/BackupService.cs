using System.Text.Json;
using System.Text.Json.Serialization;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Rules;
using DeskAI.Core.Search;

namespace DeskAI.Core.Backup;

/// <summary>One rule or saved search in a restore preview: what it is, and why it would be skipped, if it would.</summary>
public sealed record RestoreLine(string Name, string Description, string? SkipReason)
{
    public bool IsSkipped => SkipReason is not null;
}

/// <summary>What restoring a file would do right now. Nothing has been added.</summary>
public sealed record RestorePreview(string? Problem, IReadOnlyList<RestoreLine> Rules, IReadOnlyList<RestoreLine> Searches)
{
    public static RestorePreview Refused(string problem) => new(problem, [], []);

    public int RulesToAdd => Rules.Count(line => !line.IsSkipped);

    public int SearchesToAdd => Searches.Count(line => !line.IsSkipped);

    public bool AddsAnything => Problem is null && (RulesToAdd > 0 || SearchesToAdd > 0);
}

/// <summary>What a restore did. Rules added are switched off.</summary>
public sealed record RestoreOutcome(string? Problem, int RulesAdded, int SearchesAdded, IReadOnlyList<string> SkippedNames);

/// <summary>
/// Writes rules and saved searches to a backup file's text, and reads them back in.
/// </summary>
/// <remarks>
/// <para>
/// It holds two repositories and the clock, and nothing that can reach a folder, a file, a key,
/// or a setting, so a backup can only ever carry what those repositories hold. A test asserts it.
/// </para>
/// <para>
/// Reading is strict: an unknown field, a wrong version, an oversized file, or more items than
/// a backup could hold refuses the whole file in plain words, and each rule is rebuilt through
/// <see cref="RuleCodec"/> and <see cref="AutomationRule.Create"/>, so a hand-edited destination
/// such as <c>..\Windows</c> is skipped with a reason rather than stored. Restored rules always
/// arrive switched off, like a starter pack's, so restoring can never by itself move a file.
/// </para>
/// </remarks>
public sealed class BackupService(IRuleRepository rules, ISavedSearchRepository searches, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        MaxDepth = 8,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly IRuleRepository _rules = rules;
    private readonly ISavedSearchRepository _searches = searches;
    private readonly IClock _clock = clock;

    /// <summary>The text of a backup file holding every rule and saved search. Reads nothing else.</summary>
    public async Task<DeskAiBackup> ExportAsync(CancellationToken cancellationToken = default)
    {
        var storedRules = await _rules.ListAsync(cancellationToken).ConfigureAwait(false);
        var storedSearches = await _searches.ListAsync(cancellationToken).ConfigureAwait(false);
        return new DeskAiBackup(
            DeskAiBackup.CurrentVersion,
            _clock.UtcNow,
            storedRules.Select(rule => new BackupRule(
                rule.Name,
                rule.Conditions.Select(RuleCodec.Encode).ToArray(),
                RuleCodec.Encode(rule.Action))).ToArray(),
            storedSearches.Select(search => new BackupSearch(search.Name, search.Phrase, search.IsPinned)).ToArray());
    }

    public static string ToText(DeskAiBackup backup)
    {
        ArgumentNullException.ThrowIfNull(backup);
        return JsonSerializer.Serialize(backup, Json);
    }

    /// <summary>Reads a file's text as a backup, or says why it is not one.</summary>
    public static (DeskAiBackup? Backup, string? Problem) Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > DeskAiBackup.MaxBytes)
        {
            return (null, "That file is too big to be a DeskAI backup.");
        }

        DeskAiBackup? backup;
        try
        {
            backup = JsonSerializer.Deserialize<DeskAiBackup>(text, Json);
        }
        catch (JsonException)
        {
            return (null, "That file is not a DeskAI backup.");
        }
        catch (NotSupportedException)
        {
            return (null, "That file is not a DeskAI backup.");
        }

        if (backup is null || backup.Rules is null || backup.SavedSearches is null)
        {
            return (null, "That file is not a DeskAI backup.");
        }

        if (backup.Version > DeskAiBackup.CurrentVersion)
        {
            return (null, "That backup was made by a newer DeskAI. Update DeskAI to restore it.");
        }

        if (backup.Version < 1)
        {
            return (null, "That file is not a DeskAI backup.");
        }

        if (backup.Rules.Count > DeskAiBackup.MaxItems || backup.SavedSearches.Count > DeskAiBackup.MaxItems)
        {
            return (null, "That file lists more than a DeskAI backup can hold.");
        }

        return (backup, null);
    }

    /// <summary>What restoring would add and skip, against what is stored now. Adds nothing.</summary>
    public async Task<RestorePreview> PreviewAsync(string text, CancellationToken cancellationToken = default)
    {
        var (backup, problem) = Parse(text);
        if (backup is null)
        {
            return RestorePreview.Refused(problem ?? "That file is not a DeskAI backup.");
        }

        var existingRules = (await _rules.ListAsync(cancellationToken).ConfigureAwait(false))
            .Select(rule => rule.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingSearches = (await _searches.ListAsync(cancellationToken).ConfigureAwait(false))
            .Select(search => search.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ruleLines = new List<RestoreLine>();
        foreach (var entry in backup.Rules)
        {
            var (rule, reason) = Rebuild(entry);
            if (rule is null)
            {
                ruleLines.Add(new RestoreLine(entry.Name ?? "(no name)", string.Empty, reason));
                continue;
            }

            if (!existingRules.Add(rule.Name))
            {
                ruleLines.Add(new RestoreLine(rule.Name, rule.Describe(), $"You already have a rule called {rule.Name}."));
                continue;
            }

            ruleLines.Add(new RestoreLine(rule.Name, rule.Describe(), null));
        }

        var searchLines = new List<RestoreLine>();
        var room = SavedSearch.MaxSavedSearches - existingSearches.Count;
        foreach (var entry in backup.SavedSearches)
        {
            var (search, reason) = Rebuild(entry);
            if (search is null)
            {
                searchLines.Add(new RestoreLine(entry.Name ?? "(no name)", entry.Phrase ?? string.Empty, reason));
                continue;
            }

            if (!existingSearches.Add(search.Name))
            {
                searchLines.Add(new RestoreLine(search.Name, search.Phrase, $"You already have a search called {search.Name}."));
                continue;
            }

            if (room <= 0)
            {
                searchLines.Add(new RestoreLine(search.Name, search.Phrase,
                    $"DeskAI keeps at most {SavedSearch.MaxSavedSearches} saved searches. Remove one to make room."));
                continue;
            }

            room--;
            searchLines.Add(new RestoreLine(search.Name, search.Phrase, null));
        }

        return new RestorePreview(null, ruleLines, searchLines);
    }

    /// <summary>
    /// Adds what the file holds that is not already there. Rules arrive switched off.
    /// </summary>
    /// <remarks>
    /// The plan is worked out again from stored state rather than trusting a preview shown
    /// earlier, so a rule added in the meantime is skipped, never replaced.
    /// </remarks>
    public async Task<RestoreOutcome> RestoreAsync(string text, CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAsync(text, cancellationToken).ConfigureAwait(false);
        if (preview.Problem is not null)
        {
            return new RestoreOutcome(preview.Problem, 0, 0, []);
        }

        var (backup, _) = Parse(text);
        var skipped = preview.Rules.Concat(preview.Searches)
            .Where(line => line.IsSkipped)
            .Select(line => line.Name)
            .ToArray();
        var accepted = preview.Rules.Concat(preview.Searches)
            .Where(line => !line.IsSkipped)
            .Select(line => line.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rulesAdded = 0;
        foreach (var entry in backup!.Rules)
        {
            var (rule, _) = Rebuild(entry);
            if (rule is null || !accepted.Remove(rule.Name))
            {
                continue;
            }

            await _rules.SaveAsync(rule, cancellationToken).ConfigureAwait(false);
            rulesAdded++;
        }

        var pinned = (await _searches.ListAsync(cancellationToken).ConfigureAwait(false)).Count(search => search.IsPinned);
        var searchesAdded = 0;
        foreach (var entry in backup.SavedSearches)
        {
            var (search, _) = Rebuild(entry);
            if (search is null || !accepted.Remove(search.Name))
            {
                continue;
            }

            // A pin is kept only while there is room for it; the tile limit is not a backup's to exceed.
            var keepPin = search.IsPinned && pinned < SavedSearch.MaxPinned;
            await _searches.SaveAsync(search.WithPinned(false), cancellationToken).ConfigureAwait(false);
            if (keepPin)
            {
                await _searches.SetPinnedAsync(search.Id, true, cancellationToken).ConfigureAwait(false);
                pinned++;
            }

            searchesAdded++;
        }

        return new RestoreOutcome(null, rulesAdded, searchesAdded, skipped);
    }

    /// <summary>A rule from the file, through the same checks a typed one passes, always switched off.</summary>
    private static (AutomationRule? Rule, string? Reason) Rebuild(BackupRule entry)
    {
        if (entry is null || entry.Conditions is null || entry.Action is null)
        {
            return (null, "This rule is incomplete.");
        }

        try
        {
            var conditions = entry.Conditions.Select(RuleCodec.DecodeCondition).ToArray();
            var action = RuleCodec.DecodeAction(entry.Action);
            return (AutomationRule.Create(Guid.NewGuid(), entry.Name ?? string.Empty, conditions, action, isEnabled: false), null);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return (null, $"DeskAI can't use this rule: {Innermost(exception)}");
        }
    }

    private (SavedSearch? Search, string? Reason) Rebuild(BackupSearch entry)
    {
        if (entry is null)
        {
            return (null, "This search is incomplete.");
        }

        try
        {
            return (SavedSearch.Create(Guid.NewGuid(), entry.Name ?? string.Empty, entry.Phrase ?? string.Empty, _clock.UtcNow, entry.IsPinned), null);
        }
        catch (ArgumentException exception)
        {
            return (null, $"DeskAI can't use this search: {exception.Message}");
        }
    }

    private static string Innermost(Exception exception)
    {
        while (exception.InnerException is { } inner)
        {
            exception = inner;
        }

        return exception.Message;
    }
}
