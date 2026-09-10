using System.Security.Cryptography;
using System.Text;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Files;
using DeskAI.Core.Plans;
using DeskAI.Core.Recipes;
using DeskAI.Core.Roots;
using DeskAI.Core.Rules;
using FileClassification = DeskAI.Core.Classification.Classification;

namespace DeskAI.Core.Tidy;

/// <summary>
/// Works out what tidying one folder would do. It reads names, sizes, dates, and attributes,
/// and moves nothing.
/// </summary>
/// <remarks>
/// <para>
/// The folder is scanned fresh each time, never read from the remembered index, because the
/// index records how a folder looked, and a suggestion to move a file must be about the file
/// that is there now.
/// </para>
/// <para>
/// Only loose files at the top of the folder are considered. Files someone has already put in
/// a subfolder are their own organisation, and tidying must not reshuffle it.
/// </para>
/// </remarks>
public sealed class TidySuggestionService(
    IAuthorizedRootRepository roots,
    IFileScanner scanner,
    IFileClassifier classifier,
    IRuleRepository rules,
    IPlanSafetyCheck safety,
    IClock clock)
{
    /// <summary>Kept reviewable: a list longer than this is one nobody reads before approving.</summary>
    public const int MaxFilesPerTidy = 500;

    /// <summary>A file changed this recently may still be being written or downloaded.</summary>
    public static TimeSpan RecentlyChanged { get; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Deep enough to see what already sits in destination folders, so a same-name clash can be
    /// shown before anything happens; a move re-checks regardless.
    /// </summary>
    public static MetadataScanOptions ScanBounds { get; } = new(maxDepth: 3, maxEntries: 5000);

    private static readonly HashSet<string> DownloadingEndings = new(StringComparer.OrdinalIgnoreCase)
    {
        ".crdownload", ".part", ".partial", ".download", ".opdownload", ".tmp",
    };

    private static readonly FolderRecipe Recipe = TidyFolderRecipe.Create();

    /// <summary>Works out the list for one folder.</summary>
    /// <param name="aiAdvice">
    /// What AI said about files, by file ID. It is only ever input here: this service sends
    /// nothing anywhere. Where a file lands is decided in this order — the person's rules,
    /// then AI when <paramref name="mode"/> asks it about every file, then the file type, then
    /// AI for a file DeskAI cannot place — and advice about a file that has changed since is
    /// ignored.
    /// </param>
    public async Task<TidyPreview?> PreviewAsync(
        Guid rootId,
        Guid planId,
        int revision,
        IReadOnlyDictionary<Guid, SameNameChoice> choices,
        TidySuggestionMode mode,
        IReadOnlyDictionary<Guid, TidyAiAdvice> aiAdvice,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(aiAdvice);
        var root = await roots.FindAsync(rootId, cancellationToken).ConfigureAwait(false);
        if (root is null || !RootCapabilities.CanReadMetadata(root))
        {
            return null;
        }

        var now = clock.UtcNow;
        var canTidy = RootCapabilities.CanTidy(root);
        var scanned = new List<FileItem>();
        var incomplete = false;
        string? folderProblem = null;
        await foreach (var scanEvent in scanner.ScanAsync(root, ScanBounds, cancellationToken).ConfigureAwait(false))
        {
            switch (scanEvent)
            {
                case FileDiscovered discovered:
                    scanned.Add(discovered.File);
                    break;
                case ScanIssue
                {
                    RelativePath: ".",
                    Code: ScanIssueCode.RootUnavailable or ScanIssueCode.RootProtected
                        or ScanIssueCode.UnsupportedRoot or ScanIssueCode.ReparsePointSkipped,
                }:
                    folderProblem = "DeskAI could not look in this folder safely. It may have moved, or become a link.";
                    break;
                case ScanIssue:
                    incomplete = true;
                    break;
            }
        }

        if (folderProblem is not null)
        {
            return new TidyPreview(root, BuildPlan(root.Id, planId, revision, now, []), [], [], false, false, canTidy, folderProblem, [], new Dictionary<Guid, FileItem>());
        }

        var occupied = new HashSet<string>(scanned.Select(file => file.RelativePath), StringComparer.OrdinalIgnoreCase);
        var leftAlone = new List<TidyLeftAlone>();
        var candidates = new List<(FileItem File, FileClassification Class)>();
        var reachedLimit = false;
        foreach (var file in scanned
                     .Where(file => !file.RelativePath.Contains(Path.DirectorySeparatorChar))
                     .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            if (WhyLeftAlone(file, now) is { } reason)
            {
                leftAlone.Add(reason);
                continue;
            }

            if (candidates.Count == MaxFilesPerTidy)
            {
                reachedLimit = true;
                break;
            }

            candidates.Add((file, classifier.Classify(file)));
        }

        var ruleResult = RuleSetEvaluator.Evaluate(
            await rules.ListAsync(cancellationToken).ConfigureAwait(false),
            candidates.Select(item => RuleSubject.From(item.File, item.Class)).ToArray(),
            now);
        var ruleByPath = ruleResult.Proposals.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);
        var conflictByPath = ruleResult.Conflicts.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);

        var taken = new HashSet<string>(occupied, StringComparer.OrdinalIgnoreCase);
        var suggestions = new List<TidySuggestion>();
        var moves = new List<MoveFileOperation>();
        var askable = new List<FileItem>();
        var sources = new Dictionary<Guid, FileItem>();
        foreach (var (file, classification) in candidates)
        {
            var name = file.RelativePath;
            if (conflictByPath.TryGetValue(name, out var conflict))
            {
                leftAlone.Add(new(name, LeftAloneReason.RulesDisagree, conflict.Explanation));
                continue;
            }

            // Advice only counts while the file is the one AI was told about.
            var advice = aiAdvice.TryGetValue(file.Id, out var said) && said.StillAppliesTo(file) ? said : null;
            var aiFolder = advice is null ? null : Recipe.FindDestination(advice.Category);
            var typeFolder = Recipe.FindDestination(classification.Category);
            var isRulePlaced = ruleByPath.ContainsKey(name);
            if (!isRulePlaced && advice is null && (mode == TidySuggestionMode.AiForEveryFile || typeFolder is null))
            {
                askable.Add(file);
            }

            string folder;
            TidySuggestionSource source;
            string reason;
            var unsure = false;
            if (ruleByPath.TryGetValue(name, out var proposal))
            {
                folder = proposal.DestinationRelativeDirectory;
                source = TidySuggestionSource.Rule;
                reason = proposal.RuleNames.Count == 1
                    ? $"Your rule: {proposal.RuleNames[0]}"
                    : $"Your rules: {string.Join(", ", proposal.RuleNames)}";
            }
            else if (aiFolder is not null && (mode == TidySuggestionMode.AiForEveryFile || typeFolder is null))
            {
                folder = aiFolder;
                source = TidySuggestionSource.Ai;
                unsure = advice!.IsUnsure;

                // DeskAI's own words, never the AI's: a reason the AI wrote is untrusted text,
                // and a file name built to make it "explain" something alarming must not be
                // able to put that on the screen.
                reason = unsure
                    ? $"AI idea from {advice.ServiceName}, but it wasn't sure"
                    : $"AI idea from {advice.ServiceName}";
            }
            else if (typeFolder is not null)
            {
                folder = typeFolder;
                source = TidySuggestionSource.FileType;
                reason = DescribeType(name);
            }
            else
            {
                leftAlone.Add(new(name, LeftAloneReason.UnknownType, advice is null
                    ? "DeskAI does not know this kind of file yet, so it stays where it is."
                    : "Neither DeskAI nor AI could tell what this is, so it stays where it is."));
                continue;
            }

            if (FileInTheWay(folder, occupied) is { } blocker)
            {
                leftAlone.Add(new(name, LeftAloneReason.InTheWay, $"A file called {blocker} is where the {folder} folder would go."));
                continue;
            }

            var target = Path.Combine(folder, name);
            var sameName = taken.Contains(target);
            var choice = sameName ? choices.GetValueOrDefault(file.Id, SameNameChoice.Skip) : SameNameChoice.Skip;
            if (sameName && choice == SameNameChoice.KeepBoth)
            {
                if (UniqueName(target, taken) is not { } unique)
                {
                    leftAlone.Add(new(name, LeftAloneReason.TooManyWithThisName, "Too many files with this name are already there."));
                    continue;
                }

                target = unique;
            }

            Guid? moveId = null;
            if (!sameName || choice == SameNameChoice.KeepBoth)
            {
                moveId = StableId(planId, $"move:{file.Id:N}:{target}");
                moves.Add(new MoveFileOperation(
                    moveId.Value,
                    name,
                    target,
                    reason,
                    source switch
                    {
                        TidySuggestionSource.Rule => OperationProvenance.User,
                        TidySuggestionSource.Ai => advice!.Provenance,
                        _ => OperationProvenance.Rule,
                    }));
                taken.Add(target);
                sources[moveId.Value] = file;
            }

            suggestions.Add(new TidySuggestion(file.Id, name, folder, target, source, reason, moveId, sameName, choice, unsure));
        }

        var plan = BuildPlan(root.Id, planId, revision, now, moves);

        // Before permission, the policy refuses the whole plan simply because the folder may
        // not be changed yet. That is not a reason to hide suggestions from a person deciding
        // whether to allow tidying, so only a folder that can be tidied is filtered.
        if (canTidy)
        {
            var blocked = safety.FindBlocked(plan, root);
            if (blocked.Count > 0)
            {
                (suggestions, moves) = RemoveBlocked(plan, blocked, suggestions, moves, leftAlone);
                plan = BuildPlan(root.Id, planId, revision, now, moves);
            }
        }

        var kept = plan.Operations.OfType<MoveFileOperation>().Select(move => move.Id).ToHashSet();
        return new TidyPreview(root, plan, suggestions, leftAlone, reachedLimit, incomplete, canTidy, null, askable,
            sources.Where(pair => kept.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value));
    }

    private static TidyLeftAlone? WhyLeftAlone(FileItem file, DateTimeOffset now)
    {
        var name = file.RelativePath;
        if (DownloadingEndings.Contains(Path.GetExtension(name)))
        {
            return new(name, LeftAloneReason.StillDownloading, "It looks like it is still downloading.");
        }

        if ((file.Traits & FileTraits.OnlineOnly) != 0)
        {
            return new(name, LeftAloneReason.OnlineOnly, "It is stored online only. Moving it would download it first.");
        }

        if ((file.Traits & (FileTraits.Hidden | FileTraits.System)) != 0)
        {
            return new(name, LeftAloneReason.HiddenOrSystem, "It is a hidden or system file.");
        }

        return now - file.ModifiedAtUtc < RecentlyChanged
            ? new(name, LeftAloneReason.ChangedRecently, "It changed in the last few minutes, so it may still be in use.")
            : null;
    }

    private static string DescribeType(string name)
    {
        var ending = Path.GetExtension(name).TrimStart('.');
        return string.IsNullOrEmpty(ending) ? "Its kind of file" : $"{ending.ToUpperInvariant()} file";
    }

    private static string? FileInTheWay(string folder, HashSet<string> occupied)
    {
        var current = string.Empty;
        foreach (var segment in folder.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.Length == 0 ? segment : Path.Combine(current, segment);
            if (occupied.Contains(current))
            {
                return current;
            }
        }

        return null;
    }

    /// <summary>"report.pdf" becomes "report (2).pdf", the first number not already taken.</summary>
    private static string? UniqueName(string target, HashSet<string> taken)
    {
        var folder = Path.GetDirectoryName(target) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(target);
        var ending = Path.GetExtension(target);
        for (var number = 2; number <= 99; number++)
        {
            var candidate = Path.Combine(folder, $"{stem} ({number}){ending}");
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static (List<TidySuggestion>, List<MoveFileOperation>) RemoveBlocked(
        OrganizationPlan plan,
        IReadOnlyDictionary<Guid, string> blocked,
        List<TidySuggestion> suggestions,
        List<MoveFileOperation> moves,
        List<TidyLeftAlone> leftAlone)
    {
        var blockedFolders = plan.Operations.OfType<CreateDirectoryOperation>()
            .Where(operation => blocked.ContainsKey(operation.Id))
            .Select(operation => operation.DestinationRelativePath + Path.DirectorySeparatorChar)
            .ToArray();
        var wholePlan = blocked.TryGetValue(Guid.Empty, out var wholeReason) ? wholeReason : null;

        string? ReasonFor(MoveFileOperation move) =>
            wholePlan
            ?? (blocked.TryGetValue(move.Id, out var reason) ? reason : null)
            ?? (blockedFolders.Any(prefix => move.DestinationRelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                ? "DeskAI's safety rules do not allow that folder."
                : null);

        var keptMoves = new List<MoveFileOperation>();
        var refused = new Dictionary<Guid, string>();
        foreach (var move in moves)
        {
            if (ReasonFor(move) is { } reason)
            {
                refused[move.Id] = reason;
            }
            else
            {
                keptMoves.Add(move);
            }
        }

        var keptSuggestions = new List<TidySuggestion>();
        foreach (var suggestion in suggestions)
        {
            if (suggestion.MoveOperationId is { } id && refused.TryGetValue(id, out var reason))
            {
                leftAlone.Add(new(suggestion.FileName, LeftAloneReason.BlockedBySafety, reason));
            }
            else
            {
                keptSuggestions.Add(suggestion);
            }
        }

        return (keptSuggestions, keptMoves);
    }

    private OrganizationPlan BuildPlan(
        Guid rootId,
        Guid planId,
        int revision,
        DateTimeOffset now,
        IReadOnlyList<MoveFileOperation> moves)
    {
        var directories = moves
            .Select(move => Path.GetDirectoryName(move.DestinationRelativePath))
            .OfType<string>()
            .Where(path => path.Length > 0)
            .SelectMany(Expand)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path.Count(character => character == Path.DirectorySeparatorChar))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new CreateDirectoryOperation(
                StableId(planId, $"directory:{path}"), path, "A folder for tidied files", OperationProvenance.Rule));

        return OrganizationPlan.CreateDraft(
            planId,
            rootId,
            revision,
            now,
            safety.PolicyVersion,
            directories.Cast<PlanOperation>().Concat(moves));
    }

    private static IEnumerable<string> Expand(string folder)
    {
        var current = string.Empty;
        foreach (var segment in folder.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.Length == 0 ? segment : Path.Combine(current, segment);
            yield return current;
        }
    }

    private static Guid StableId(Guid planId, string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{planId:N}:{value.ToUpperInvariant()}"));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
