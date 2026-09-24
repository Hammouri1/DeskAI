using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Studio;

/// <summary>
/// A grouping request prepared and shown to the person but not yet sent. Sending sends this same
/// request, so what was shown is what goes.
/// </summary>
/// <param name="Lines">One line per numbered item, exactly what the AI will see about it.</param>
/// <param name="PathsByNumber">The Desktop-relative path of item N at index N-1. Never sent.</param>
/// <param name="LeftOutItems">Items beyond the bounds: sorted by DeskAI's own guess, never sent.</param>
public sealed record DesktopGroupQuestion(
    Guid RootId,
    AiMode Mode,
    string ProviderId,
    string ServiceName,
    string Destination,
    IReadOnlyList<string> Lines,
    AiGroupingRequest Request,
    IReadOnlyList<string> PathsByNumber,
    IReadOnlyList<DesktopItem> LeftOutItems)
{
    public int LeftOut => LeftOutItems.Count;

    /// <summary>True when some folders were too full to look all the way inside.</summary>
    public bool StoppedEarly { get; init; }
}

/// <summary>A prepared question, or the plain reason there is none.</summary>
public sealed record DesktopGroupPreparation(DesktopGroupQuestion? Question, string Explanation);

/// <summary>What happened, the board as it now stands (or null), and a message for the page.</summary>
public sealed record DesktopGroupResult(bool Succeeded, DesktopGroupBoard? Board, string Message);

/// <summary>
/// Find groups (ADR 0042): sorts what sits on the connected Desktop into a board of at most eight
/// groups, by AI after the person sees the exact list, or by DeskAI's own guess.
/// </summary>
/// <remarks>
/// <para>
/// Two steps with the person in between, like reading a sentence: <see cref="PrepareAsync"/>
/// builds the numbered list and sends nothing; only <see cref="SendAsync"/>, after Send, sends
/// that same request, and only if the AI choice and sharing choices are still the ones shown.
/// </para>
/// <para>
/// The board is advice only. This service looks at the Desktop through the read-only scanner and
/// holds no executor, journal, writer, or setting changer; a test fixes that.
/// </para>
/// </remarks>
public sealed class DesktopGroupingService(
    PersonalFolderPolicy personalFolders,
    IAuthorizedRootRepository roots,
    DesktopLookService look,
    LocalDesktopGrouper localGrouper,
    IAiSettingsRepository aiSettings,
    IOrganizationSuggestionProvider ai,
    IDesktopGroupRepository boards,
    IClock clock)
{
    public const string NothingToSort = "There is nothing on your Desktop to sort.";
    public const string NotConnected = "Connect your Desktop first.";
    public const string GuessMessage = "Sorted by DeskAI's own simpler guess from the kinds of files. You can change any group.";
    public const string SharingNeeded = "To let AI sort your Desktop, allow sharing file types, file names, and folder names in Privacy and AI.";
    public const string AiNeeded = "Turn on AI in Privacy and AI first, or press Use DeskAI's guess.";
    public const string PartlyLooked = "Some folders were too full to look all the way inside, so they are sorted by what DeskAI saw first.";

    /// <summary>Which AI the person set up, if any, for the page's button and labels.</summary>
    public async Task<AiTarget> GetAiAsync(CancellationToken cancellationToken = default) =>
        AiTarget.Of(await aiSettings.LoadAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>The connected folder that is the person's Desktop, or null.</summary>
    public async Task<AuthorizedRoot?> FindDesktopAsync(CancellationToken cancellationToken = default)
    {
        if (personalFolders.Find(PersonalFolderKind.Desktop) is not { } desktop)
        {
            return null;
        }

        var connected = await roots.ListAsync(cancellationToken).ConfigureAwait(false);
        return connected.FirstOrDefault(root => SamePath(root.CanonicalPath, desktop.Path));
    }

    /// <summary>
    /// The saved board with anything no longer on the Desktop taken off and anything new put under
    /// Not sure, saved again when that changed it.
    /// </summary>
    public async Task<DesktopGroupResult> LoadBoardAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        if (await DesktopAsync(rootId, cancellationToken).ConfigureAwait(false) is not { } root)
        {
            return new(false, null, NotConnected);
        }

        var board = await boards.LoadAsync(rootId, cancellationToken).ConfigureAwait(false);
        if (board is null)
        {
            return new(true, null, string.Empty);
        }

        var seen = await look.LookAsync(root, cancellationToken).ConfigureAwait(false);
        if (seen.Problem is not null)
        {
            return new(false, board, seen.Problem);
        }

        var present = seen.Everything.Select(item => item.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var onBoard = board.Groups.SelectMany(g => g.Items).Concat(board.NotSure).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var gone = onBoard.Count(item => !present.Contains(item));
        var added = seen.Everything.Select(item => item.RelativePath).Where(path => !onBoard.Contains(path)).ToList();
        if (gone == 0 && added.Count == 0)
        {
            return new(true, board, string.Empty);
        }

        var updated = board with
        {
            Groups = board.Groups.Select(g => g with { Items = g.Items.Where(present.Contains).ToList() }).ToList(),
            NotSure = board.NotSure.Where(present.Contains).Concat(added).ToList(),
            Folders = FoldersIn(seen.Everything),
        };
        await boards.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        var notes = new List<string>();
        if (gone > 0)
        {
            notes.Add(gone == 1
                ? "1 thing is no longer on your Desktop, so it was taken off the board."
                : $"{gone} things are no longer on your Desktop, so they were taken off the board.");
        }

        if (added.Count > 0)
        {
            notes.Add(added.Count == 1 ? "1 new thing is under Not sure." : $"{added.Count} new things are under Not sure.");
        }

        return new(true, updated, string.Join(' ', notes));
    }

    /// <summary>Builds the numbered list and says who would see it. Sends nothing.</summary>
    public async Task<DesktopGroupPreparation> PrepareAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        if (await DesktopAsync(rootId, cancellationToken).ConfigureAwait(false) is not { } root)
        {
            return new(null, NotConnected);
        }

        var settings = await aiSettings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var target = AiTarget.Of(settings);
        if (!target.IsSetUp)
        {
            return new(null, AiNeeded);
        }

        if (!SharingAllows(settings))
        {
            return new(null, SharingNeeded);
        }

        var seen = await look.LookAsync(root, cancellationToken).ConfigureAwait(false);
        if (seen.Problem is not null)
        {
            return new(null, seen.Problem);
        }

        if (seen.Items.Count == 0)
        {
            return new(null, NothingToSort);
        }

        var limits = AiGroupingRequest.DefaultLimits with { Timeout = TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 5, 120)) };
        var (sent, items) = WithinSizeLimit(seen.Items, limits.MaximumRequestBytes);
        var request = new AiGroupingRequest(AiGroupingRequest.CurrentSchemaVersion, Guid.NewGuid(), items, limits);
        var leftOut = seen.Items.Skip(sent.Count).Concat(seen.LeftOutItems).ToList();
        var explanation = $"{target.Name} at {target.Destination} will see only this list: names and kinds of files. Not what is inside them, and not where they are.";
        if (seen.StoppedEarly)
        {
            explanation += $" {PartlyLooked}";
        }

        if (leftOut.Count > 0)
        {
            explanation += $" {leftOut.Count} more will be sorted by DeskAI's own guess and not sent.";
        }

        return new(
            new DesktopGroupQuestion(
                root.Id,
                settings.Mode,
                settings.ProviderId,
                target.Name,
                target.Destination,
                sent.Select(Line).ToList(),
                request,
                sent.Select(item => item.RelativePath).ToList(),
                leftOut)
            {
                StoppedEarly = seen.StoppedEarly,
            },
            explanation);
    }

    /// <summary>
    /// Sends a prepared question after checking that the Desktop, the AI choice, and the sharing
    /// choices are still the ones the person saw, and reads the answer strictly into a board.
    /// </summary>
    public async Task<DesktopGroupResult> SendAsync(DesktopGroupQuestion question, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(question);
        var existing = await boards.LoadAsync(question.RootId, cancellationToken).ConfigureAwait(false);
        if (await DesktopAsync(question.RootId, cancellationToken).ConfigureAwait(false) is null)
        {
            return new(false, existing, NotConnected);
        }

        var settings = await aiSettings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var target = AiTarget.Of(settings);
        if (!target.IsSetUp ||
            settings.Mode != question.Mode ||
            !string.Equals(settings.ProviderId, question.ProviderId, StringComparison.Ordinal) ||
            !string.Equals(target.Destination, question.Destination, StringComparison.Ordinal) ||
            !SharingAllows(settings))
        {
            return new(false, existing, "Your AI or sharing choices changed since you looked, so nothing was sent. Try again to see what would be sent.");
        }

        var response = await ai.GroupItemsAsync(question.Request, cancellationToken).ConfigureAwait(false);
        if (!response.IsAvailable)
        {
            return new(false, existing, response.Message);
        }

        var reading = DesktopGroupReading.Read(response.Json!, question.PathsByNumber.Count, question.Request.Limits.MaximumResponseBytes);
        if (!reading.IsValid)
        {
            return new(false, existing, $"{question.ServiceName}'s answer did not pass DeskAI's checks, so it was ignored. {reading.Problem}");
        }

        var groups = reading.Groups
            .Select(g => (Name: g.Name, Items: g.Numbers.Select(n => question.PathsByNumber[n - 1]).ToList()))
            .ToList();
        var mentioned = reading.Groups.SelectMany(g => g.Numbers).ToHashSet();
        var notSure = question.PathsByNumber.Where((_, index) => !mentioned.Contains(index + 1)).ToList();

        var message = $"Grouped by {question.ServiceName}. You can change any group.";
        if (question.StoppedEarly)
        {
            message += $" {PartlyLooked}";
        }

        if (question.LeftOut > 0)
        {
            var guessedGroups = localGrouper.Group(question.LeftOutItems, out var guessedUnsure);
            foreach (var guessed in guessedGroups)
            {
                var match = groups.FindIndex(g => string.Equals(g.Name, guessed.Name, StringComparison.OrdinalIgnoreCase));
                if (match >= 0)
                {
                    groups[match].Items.AddRange(guessed.Items);
                }
                else if (groups.Count < DesktopGroupBoard.MaxGroups)
                {
                    groups.Add((guessed.Name, guessed.Items.ToList()));
                }
                else
                {
                    notSure.AddRange(guessed.Items);
                }
            }

            notSure.AddRange(guessedUnsure);
            message += $" {question.LeftOut} more were sorted by DeskAI's own guess.";
        }

        var board = new DesktopGroupBoard(
            question.RootId,
            groups.Select(g => new DesktopGroup(g.Name, g.Items)).ToList(),
            notSure,
            DesktopGroupSource.Ai,
            clock.UtcNow)
        {
            Folders = question.Request.Items.Where(item => item.Kind == "folder").Select(item => question.PathsByNumber[item.Number - 1])
                .Concat(question.LeftOutItems.Where(item => item.IsFolder).Select(item => item.RelativePath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            MadeBy = question.ServiceName,
        };
        await boards.SaveAsync(board, cancellationToken).ConfigureAwait(false);
        return new(true, board, message);
    }

    /// <summary>Fills the board with DeskAI's own guess from the kinds of files. Uses no AI.</summary>
    public async Task<DesktopGroupResult> GuessAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        if (await DesktopAsync(rootId, cancellationToken).ConfigureAwait(false) is not { } root)
        {
            return new(false, null, NotConnected);
        }

        var existing = await boards.LoadAsync(rootId, cancellationToken).ConfigureAwait(false);
        var seen = await look.LookAsync(root, cancellationToken).ConfigureAwait(false);
        if (seen.Problem is not null)
        {
            return new(false, existing, seen.Problem);
        }

        var everything = seen.Everything.ToList();
        if (everything.Count == 0)
        {
            return new(false, existing, NothingToSort);
        }

        var groups = localGrouper.Group(everything, out var notSure);
        var board = new DesktopGroupBoard(rootId, groups, notSure, DesktopGroupSource.LocalGuess, clock.UtcNow) { Folders = FoldersIn(everything) };
        await boards.SaveAsync(board, cancellationToken).ConfigureAwait(false);
        return new(true, board, seen.StoppedEarly ? $"{GuessMessage} {PartlyLooked}" : GuessMessage);
    }

    public Task<DesktopGroupResult> RenameAsync(Guid rootId, string group, string newName, CancellationToken cancellationToken = default) =>
        EditAsync(rootId, board =>
        {
            var index = IndexOf(board, group);
            if (index < 0)
            {
                return (null, $"There is no group called {group}.");
            }

            var name = newName?.Trim() ?? string.Empty;
            var others = board.Groups.Where((_, i) => i != index).Select(g => g.Name);
            if (DesktopGroupBoard.CheckName(name, others) is { } problem)
            {
                return (null, problem);
            }

            var groups = board.Groups.ToList();
            groups[index] = groups[index] with { Name = name };
            return (board with { Groups = groups }, $"Renamed {group} to {name}.");
        }, cancellationToken);

    public Task<DesktopGroupResult> MergeAsync(Guid rootId, string from, string into, CancellationToken cancellationToken = default) =>
        EditAsync(rootId, board =>
        {
            var fromIndex = IndexOf(board, from);
            var intoIndex = IndexOf(board, into);
            if (fromIndex < 0 || intoIndex < 0 || fromIndex == intoIndex)
            {
                return (null, "Choose two different groups to put together.");
            }

            var groups = board.Groups.ToList();
            groups[intoIndex] = groups[intoIndex] with { Items = [.. groups[intoIndex].Items, .. groups[fromIndex].Items] };
            groups.RemoveAt(fromIndex);
            return (board with { Groups = groups }, $"Put {from} into {into}.");
        }, cancellationToken);

    /// <summary>Moves one item to another group, or to Not sure when <paramref name="toGroup"/> is null.</summary>
    public Task<DesktopGroupResult> MoveAsync(Guid rootId, string itemPath, string? toGroup, CancellationToken cancellationToken = default) =>
        EditAsync(rootId, board =>
        {
            var isOnBoard = board.NotSure.Contains(itemPath, StringComparer.OrdinalIgnoreCase) ||
                board.Groups.Any(g => g.Items.Contains(itemPath, StringComparer.OrdinalIgnoreCase));
            var targetIndex = toGroup is null ? -1 : IndexOf(board, toGroup);
            if (!isOnBoard || (toGroup is not null && targetIndex < 0))
            {
                return (null, "That can't be moved there.");
            }

            bool Other(string item) => !string.Equals(item, itemPath, StringComparison.OrdinalIgnoreCase);
            var groups = board.Groups.Select(g => g with { Items = g.Items.Where(Other).ToList() }).ToList();
            var notSure = board.NotSure.Where(Other).ToList();
            if (targetIndex < 0)
            {
                notSure.Add(itemPath);
            }
            else
            {
                groups[targetIndex] = groups[targetIndex] with { Items = [.. groups[targetIndex].Items, itemPath] };
            }

            return (board with { Groups = groups, NotSure = notSure }, $"Moved {Path.GetFileName(itemPath)} to {toGroup ?? DesktopGroupBoard.NotSureName}.");
        }, cancellationToken);

    private async Task<DesktopGroupResult> EditAsync(
        Guid rootId,
        Func<DesktopGroupBoard, (DesktopGroupBoard? Changed, string Message)> edit,
        CancellationToken cancellationToken)
    {
        if (await DesktopAsync(rootId, cancellationToken).ConfigureAwait(false) is null)
        {
            return new(false, null, NotConnected);
        }

        var board = await boards.LoadAsync(rootId, cancellationToken).ConfigureAwait(false);
        if (board is null)
        {
            return new(false, null, "Find groups first.");
        }

        var (changed, message) = edit(board);
        if (changed is null || changed.Groups.Count > DesktopGroupBoard.MaxGroups)
        {
            return new(false, board, message);
        }

        await boards.SaveAsync(changed, cancellationToken).ConfigureAwait(false);
        return new(true, changed, message);
    }

    /// <summary>The root, only when it is the connected Desktop. Any other folder is refused.</summary>
    private async Task<AuthorizedRoot?> DesktopAsync(Guid rootId, CancellationToken cancellationToken)
    {
        var desktop = await FindDesktopAsync(cancellationToken).ConfigureAwait(false);
        return desktop?.Id == rootId ? desktop : null;
    }

    /// <summary>Local AI stays on this computer; online AI needs every category a grouping request reveals.</summary>
    private static bool SharingAllows(AiSettings settings) =>
        settings.Mode != AiMode.Cloud || AiGroupingRequest.Discloses.All(settings.CloudDisclosures.Contains);

    private static int IndexOf(DesktopGroupBoard board, string name)
    {
        for (var index = 0; index < board.Groups.Count; index++)
        {
            if (string.Equals(board.Groups[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static string Line(DesktopItem item)
    {
        if (!item.IsFolder)
        {
            return $"File \"{item.Name}\"";
        }

        var types = string.Join(", ", item.Types.Select(t => $"{t.Count} {t.Ending}"));
        var names = string.Join(", ", item.SampleNames);
        var details = (types.Length, names.Length) switch
        {
            ( > 0, > 0) => $"{types}; {names}",
            ( > 0, 0) => types,
            (0, > 0) => names,
            _ => "empty",
        };
        return $"Folder \"{item.Name}\": {details}";
    }

    /// <summary>Room kept for the fixed prompt words and the request wrapper around the item list.</summary>
    private const int PromptAllowanceBytes = 4 * 1024;

    private static readonly System.Text.Json.JsonSerializerOptions ItemJson = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// The items, in order, that fit the request's size limit once written the way the AI
    /// connection writes them: the list goes into the prompt as JSON, and the prompt into the
    /// request as a JSON string, so a non-English letter can take seven bytes. Checking here means
    /// Send never refuses a list the person already approved; the rest get DeskAI's own guess.
    /// </summary>
    private static (List<DesktopItem> Sent, List<AiGroupingItem> Items) WithinSizeLimit(IReadOnlyList<DesktopItem> candidates, int maximumRequestBytes)
    {
        var sent = new List<DesktopItem>();
        var items = new List<AiGroupingItem>();
        var used = PromptAllowanceBytes;
        foreach (var candidate in candidates)
        {
            var item = new AiGroupingItem(
                items.Count + 1,
                candidate.IsFolder ? "folder" : "file",
                candidate.Name,
                candidate.Types.Select(t => $"{t.Count} {t.Ending}").ToList(),
                candidate.SampleNames);
            var twiceWritten = System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonSerializer.Serialize(item, ItemJson));
            used += System.Text.Encoding.UTF8.GetByteCount(twiceWritten) + 1;
            if (used > maximumRequestBytes)
            {
                break;
            }

            sent.Add(candidate);
            items.Add(item);
        }

        return (sent, items);
    }

    private static HashSet<string> FoldersIn(IEnumerable<DesktopItem> items) =>
        items.Where(item => item.IsFolder).Select(item => item.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool SamePath(string first, string second) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(first),
            Path.TrimEndingDirectorySeparator(second),
            StringComparison.OrdinalIgnoreCase);
}
