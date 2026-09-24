using System.Text;
using System.Text.Json;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Classification;
using DeskAI.Core.Files;
using DeskAI.Core.Templates;

namespace DeskAI.Core.Studio;

/// <summary>One group on the board. Items are paths relative to the connected Desktop.</summary>
public sealed record DesktopGroup(string Name, IReadOnlyList<string> Items);

public enum DesktopGroupSource
{
    Ai,
    LocalGuess,
}

/// <summary>
/// The Find groups board for one connected Desktop (ADR 0042). It is advice only: nothing reads
/// it to change a file or a Windows setting in this step.
/// </summary>
public sealed record DesktopGroupBoard(
    Guid RootId,
    IReadOnlyList<DesktopGroup> Groups,
    IReadOnlyList<string> NotSure,
    DesktopGroupSource Source,
    DateTimeOffset MadeAtUtc)
{
    public const int MaxGroups = 8;
    public const string NotSureName = "Not sure";

    /// <summary>Which items were folders when last looked at, so the page never has to look at the disk.</summary>
    public IReadOnlySet<string> Folders { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The AI service that made an AI board, so the page names who saw the list even after AI is changed.</summary>
    public string? MadeBy { get; init; }

    /// <summary>Why a group name cannot be used on this board, or null when it can.</summary>
    public static string? CheckName(string name, IEnumerable<string> otherNames)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (FolderNameCheck.Check(name) is { } reason)
        {
            return reason;
        }

        if (string.Equals(name, NotSureName, StringComparison.OrdinalIgnoreCase))
        {
            return $"\"{NotSureName}\" is kept for things without a group.";
        }

        return otherNames.Contains(name, StringComparer.OrdinalIgnoreCase)
            ? $"There is already a group called {name}."
            : null;
    }
}

public sealed record DesktopGroupReadingResult(
    bool IsValid,
    IReadOnlyList<(string Name, IReadOnlyList<int> Numbers)> Groups,
    string? Problem);

/// <summary>
/// Reads an AI grouping answer strictly. The answer is untrusted text: anything outside the one
/// accepted shape is refused as a whole, so a partly sensible answer never slips through.
/// </summary>
public static class DesktopGroupReading
{
    public const string SchemaVersion = "1";
    private const string OffShape = "The AI answer was not in the shape DeskAI asked for.";

    public static DesktopGroupReadingResult Read(string json, int itemCount, int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (maxBytes < 1 || Encoding.UTF8.GetByteCount(json) > maxBytes)
        {
            return Refused("The AI answer was too large.");
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            return ReadRoot(document.RootElement, itemCount);
        }
        catch (JsonException)
        {
            return Refused(OffShape);
        }
    }

    private static DesktopGroupReadingResult ReadRoot(JsonElement root, int itemCount)
    {
        if (!HasExactly(root, "schemaVersion", "groups")
            || root.GetProperty("schemaVersion") is not { ValueKind: JsonValueKind.String } version
            || version.GetString() != SchemaVersion
            || root.GetProperty("groups") is not { ValueKind: JsonValueKind.Array } groups)
        {
            return Refused(OffShape);
        }

        if (groups.GetArrayLength() > DesktopGroupBoard.MaxGroups)
        {
            return Refused($"The AI answer named more than {DesktopGroupBoard.MaxGroups} groups.");
        }

        var read = new List<(string Name, IReadOnlyList<int> Numbers)>();
        var usedNumbers = new HashSet<int>();
        foreach (var group in groups.EnumerateArray())
        {
            if (!HasExactly(group, "name", "items")
                || group.GetProperty("name") is not { ValueKind: JsonValueKind.String } nameElement
                || group.GetProperty("items") is not { ValueKind: JsonValueKind.Array } items)
            {
                return Refused(OffShape);
            }

            var name = nameElement.GetString()!;
            if (DesktopGroupBoard.CheckName(name, read.Select(g => g.Name)) is not null)
            {
                return Refused("The AI answer used a group name DeskAI can't accept.");
            }

            var numbers = new List<int>();
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number
                    || !item.TryGetInt32(out var number)
                    || number < 1
                    || number > itemCount
                    || !usedNumbers.Add(number))
                {
                    return Refused(OffShape);
                }

                numbers.Add(number);
            }

            read.Add((name, numbers));
        }

        return new DesktopGroupReadingResult(true, read, null);
    }

    /// <summary>True when the element is an object with exactly these properties, each once.</summary>
    private static bool HasExactly(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
            {
                return false;
            }
        }

        return seen.Count == names.Length;
    }

    private static DesktopGroupReadingResult Refused(string problem) => new(false, [], problem);
}

/// <summary>
/// DeskAI's own simpler guess, used without AI: a folder goes by the kind of file it mostly
/// holds, a loose file by its own kind. Reads only the summary it is given.
/// </summary>
public sealed class LocalDesktopGrouper(IFileClassifier classifier)
{
    private static readonly Guid ProbeId = Guid.Parse("5d1f4d0e-8c4b-4f0e-a3f3-1b2f6f7c9a10");

    public IReadOnlyList<DesktopGroup> Group(IReadOnlyList<DesktopItem> items, out IReadOnlyList<string> notSure)
    {
        ArgumentNullException.ThrowIfNull(items);
        var order = new List<string>();
        var members = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var unsure = new List<string>();
        foreach (var item in items)
        {
            var ending = item.IsFolder
                ? item.Types.OrderByDescending(t => t.Count).Select(t => t.Ending).FirstOrDefault()
                : Path.GetExtension(item.Name);
            var name = string.IsNullOrEmpty(ending) ? null : GroupNameFor(Classify(ending));
            if (name is null)
            {
                unsure.Add(item.RelativePath);
                continue;
            }

            if (!members.TryGetValue(name, out var list))
            {
                members[name] = list = [];
                order.Add(name);
            }

            list.Add(item.RelativePath);
        }

        notSure = unsure;
        return order.Select(name => new DesktopGroup(name, members[name])).ToList();
    }

    private FileCategory Classify(string ending) =>
        classifier.Classify(new FileItem(ProbeId, "x" + ending, FileKind.Unknown, 0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)).Category;

    private static string? GroupNameFor(FileCategory category) => category switch
    {
        FileCategory.SourceCode => "Coding",
        FileCategory.Documents or FileCategory.Presentations or FileCategory.Spreadsheets => "Documents",
        FileCategory.Images or FileCategory.Screenshots => "Pictures",
        FileCategory.Videos => "Videos",
        FileCategory.Audio => "Music",
        FileCategory.Archives or FileCategory.Installers => "Downloads",
        FileCategory.Data => "Data",
        _ => null,
    };
}
