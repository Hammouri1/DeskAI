using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeskAI.Core.Classification;

namespace DeskAI.Core.Ai;

/// <summary>What strict reading of an AI sentence answer produced: a sentence in DeskAI's own words, or a reason.</summary>
public sealed record AiSentenceReadingResult(string? Sentence, string? Problem)
{
    public bool IsValid => Sentence is not null;
}

/// <summary>What kind of thing a question to Ask DeskAI turned out to be.</summary>
public enum AskIntentKind
{
    /// <summary>Find files; <see cref="AskIntent.SearchSentence"/> holds the search in DeskAI's words.</summary>
    Search,

    /// <summary>What is taking space, or how big things are.</summary>
    Space,

    /// <summary>Tidy or organize a folder.</summary>
    Tidy,

    /// <summary>The AI could not tell. DeskAI says so and suggests what can be asked.</summary>
    Unsure,
}

/// <summary>
/// A question read into a kind, an optional folder name as the person wrote it, and, for a
/// search, the search in DeskAI's own words. Everything DeskAI then does with it is its own
/// deterministic work on the local index; nothing here is an instruction.
/// </summary>
public sealed record AskIntent(AskIntentKind Kind, string? FolderName, string? SearchSentence);

/// <summary>A read question, or the reason it was refused.</summary>
public sealed record AskIntentReading(AskIntent? Intent, string? Problem)
{
    public bool IsValid => Intent is not null;
}

/// <summary>
/// Turns the JSON an AI answered with into a sentence in DeskAI's own fixed vocabulary — the
/// words <c>NaturalLanguageQueryTranslator</c> and <c>RuleDraftTranslator</c> already read.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole trick of AI sentence reading (V1.1, ADR 0033). AI never produces a query
/// or a rule. It produces a few typed facts; DeskAI writes those facts as a sentence such as
/// "photos larger than 5 mb last 30 days holiday" or "move .pdf statement into Bank"; and that
/// sentence goes through exactly the reader a typed one does, is shown in the box, and can be
/// edited. AI therefore gains no reach a person typing does not have, and a saved or pinned
/// search still holds a sentence DeskAI can read on its own tomorrow.
/// </para>
/// <para>
/// Reading is strict: unknown properties, another schema version, an unknown category, a
/// negative size, an out-of-range number, or a destination that is not a plain folder name
/// refuse the whole answer. Free text is cleaned to letters, digits, spaces, and hyphens, so a
/// path, a quote, or an instruction hidden in it becomes harmless words at most.
/// </para>
/// </remarks>
public static class AiSentenceReading
{
    /// <summary>The longest free text carried into a sentence. Longer is cut, not refused: it is only words to look for.</summary>
    public const int MaxTextLength = 64;

    public const int MaxDays = 3650;

    private const int MaxEndings = 8;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
    };

    /// <summary>The category words each reader understands, keyed by the name the AI may answer with.</summary>
    private static readonly IReadOnlyDictionary<string, string> CategoryWords = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(FileCategory.Screenshots)] = "screenshots",
        [nameof(FileCategory.Images)] = "photos",
        [nameof(FileCategory.Videos)] = "videos",
        [nameof(FileCategory.Audio)] = "music",
        [nameof(FileCategory.Spreadsheets)] = "spreadsheets",
        [nameof(FileCategory.Presentations)] = "slides",
        [nameof(FileCategory.Archives)] = "archives",
        [nameof(FileCategory.Installers)] = "installers",
        [nameof(FileCategory.Documents)] = "documents",
    };

    /// <summary>The names the AI may answer with, in the order the prompt lists them.</summary>
    public static IReadOnlyList<string> CategoryNames { get; } = CategoryWords.Keys.ToArray();

    /// <summary>Words that would be read as "and put it here" by the rule reader, so they never stay in free text.</summary>
    private static readonly HashSet<string> DestinationMarkers = new(StringComparer.OrdinalIgnoreCase) { "to", "into", "in" };

    public static AiSentenceReadingResult Read(SentenceTask task, string json, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (maximumBytes < 1 || Encoding.UTF8.GetByteCount(json) > maximumBytes)
        {
            return Refused("The AI answer was too large.");
        }

        try
        {
            return task switch
            {
                SentenceTask.SearchPhrase => ReadSearch(json),
                SentenceTask.RuleSentence => ReadRule(json),
                _ => Refused("Unknown task."),
            };
        }
        catch (JsonException)
        {
            return Refused("The AI answer was not in the shape DeskAI asked for.");
        }
    }

    /// <summary>
    /// Reads a question's answer (ADR 0035). The kind must be one of four words; the folder is
    /// reduced to harmless words like any free text; a search, when present, is read exactly as
    /// a search answer is.
    /// </summary>
    public static AskIntentReading ReadQuestion(string json, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (maximumBytes < 1 || Encoding.UTF8.GetByteCount(json) > maximumBytes)
        {
            return new(null, "The AI answer was too large.");
        }

        QuestionAnswer? answer;
        try
        {
            answer = JsonSerializer.Deserialize<QuestionAnswer>(json, Options);
        }
        catch (JsonException)
        {
            return new(null, "The AI answer was not in the shape DeskAI asked for.");
        }

        if (answer is null || answer.SchemaVersion != AiSentenceRequest.CurrentSchemaVersion)
        {
            return new(null, "The AI answer was not in the shape DeskAI asked for.");
        }

        var kind = answer.Kind switch
        {
            "search" => AskIntentKind.Search,
            "space" => AskIntentKind.Space,
            "tidy" => AskIntentKind.Tidy,
            "unsure" => AskIntentKind.Unsure,
            _ => (AskIntentKind?)null,
        };
        if (kind is null)
        {
            return new(null, "The AI named a kind of question DeskAI does not know.");
        }

        var folder = CleanText(answer.Folder);
        if (kind != AskIntentKind.Search)
        {
            return new(new AskIntent(kind.Value, folder, null), null);
        }

        if (answer.Search is null)
        {
            return new(null, "The AI said this was a search but gave nothing to search for.");
        }

        var search = ReadSearch(answer.Search);
        return search.IsValid
            ? new(new AskIntent(AskIntentKind.Search, folder, search.Sentence), null)
            : new(null, search.Problem);
    }

    private static AiSentenceReadingResult ReadSearch(string json) =>
        ReadSearch(JsonSerializer.Deserialize<SearchAnswer>(json, Options));

    private static AiSentenceReadingResult ReadSearch(SearchAnswer? answer)
    {
        if (answer is null || answer.SchemaVersion != AiSentenceRequest.CurrentSchemaVersion)
        {
            return Refused("The AI answer was not in the shape DeskAI asked for.");
        }

        var parts = new List<string>();
        if (!TryAddEndings(answer.Endings, parts, out var problem)
            || !TryAddCategories(answer.Categories, parts, out problem)
            || !TryAddSize(answer.LargerThanBytes, "larger than", forRule: false, parts, out problem)
            || !TryAddSize(answer.SmallerThanBytes, "smaller than", forRule: false, parts, out problem)
            || !TryAddDays(answer.ChangedInLastDays, "last {0} days", parts, out problem))
        {
            return Refused(problem);
        }

        if (CleanText(answer.Text) is { } text)
        {
            parts.Add(text);
        }

        return parts.Count == 0
            ? Refused("The AI did not find anything to search for in that sentence.")
            : new(string.Join(' ', parts), null);
    }

    private static AiSentenceReadingResult ReadRule(string json)
    {
        var answer = JsonSerializer.Deserialize<RuleAnswer>(json, Options);
        if (answer is null || answer.SchemaVersion != AiSentenceRequest.CurrentSchemaVersion)
        {
            return Refused("The AI answer was not in the shape DeskAI asked for.");
        }

        var parts = new List<string> { "move" };
        if (!TryAddEndings(answer.Ending is null ? [] : [answer.Ending], parts, out var problem)
            || !TryAddCategories(answer.Category is null ? [] : [answer.Category], parts, out problem)
            || !TryAddSize(answer.LargerThanBytes, "bigger than", forRule: true, parts, out problem)
            || !TryAddSize(answer.SmallerThanBytes, "smaller than", forRule: true, parts, out problem)
            || !TryAddDays(answer.OlderThanDays, "older than {0} days", parts, out problem))
        {
            return Refused(problem);
        }

        if (CleanText(answer.NameContains) is { } text)
        {
            parts.Add(text);
        }

        if (parts.Count == 1)
        {
            return Refused("The AI did not find anything to look for in that sentence.");
        }

        if (answer.Destination is { } destination)
        {
            if (!TryCleanDestination(destination, out var folder))
            {
                return Refused("The AI named a folder DeskAI cannot use. A destination must be a plain folder name inside the connected folder.");
            }

            parts.Add("into");
            parts.Add(folder);
        }

        return new(string.Join(' ', parts), null);
    }

    private static bool TryAddEndings(IReadOnlyList<string>? endings, List<string> parts, out string problem)
    {
        problem = string.Empty;
        if (endings is null)
        {
            return true;
        }

        if (endings.Count > MaxEndings)
        {
            problem = "The AI named too many file endings.";
            return false;
        }

        foreach (var raw in endings)
        {
            var ending = raw?.Trim().TrimStart('.').ToLowerInvariant();
            if (string.IsNullOrEmpty(ending) || ending.Length > 8 || !ending.All(character => char.IsAsciiLetterOrDigit(character)))
            {
                problem = "The AI named something that is not a file ending.";
                return false;
            }

            parts.Add("." + ending);
        }

        return true;
    }

    private static bool TryAddCategories(IReadOnlyList<string>? categories, List<string> parts, out string problem)
    {
        problem = string.Empty;
        if (categories is null)
        {
            return true;
        }

        foreach (var category in categories)
        {
            if (category is null || !CategoryWords.TryGetValue(category, out var word))
            {
                problem = "The AI named a kind of file DeskAI does not know.";
                return false;
            }

            if (!parts.Contains(word, StringComparer.Ordinal))
            {
                parts.Add(word);
            }
        }

        return true;
    }

    private static bool TryAddSize(long? bytes, string words, bool forRule, List<string> parts, out string problem)
    {
        problem = string.Empty;
        if (bytes is null)
        {
            return true;
        }

        if (bytes <= 0)
        {
            problem = "The AI gave a size DeskAI cannot use.";
            return false;
        }

        parts.Add($"{words} {DescribeSize(bytes.Value, forRule)}");
        return true;
    }

    private static bool TryAddDays(int? days, string format, List<string> parts, out string problem)
    {
        problem = string.Empty;
        if (days is null)
        {
            return true;
        }

        if (days is < 1 or > MaxDays)
        {
            problem = "The AI gave a number of days DeskAI cannot use.";
            return false;
        }

        parts.Add(string.Format(CultureInfo.InvariantCulture, format, days.Value));
        return true;
    }

    /// <summary>A size in the largest whole unit, in the words each reader accepts ("mb" for search, "mb" or "bytes" for rules).</summary>
    private static string DescribeSize(long bytes, bool forRule)
    {
        const long Kb = 1024;
        const long Mb = Kb * 1024;
        const long Gb = Mb * 1024;
        return bytes switch
        {
            _ when bytes % Gb == 0 => $"{bytes / Gb} gb",
            _ when bytes % Mb == 0 => $"{bytes / Mb} mb",
            _ when bytes % Kb == 0 => $"{bytes / Kb} kb",
            _ => forRule ? $"{bytes} bytes" : $"{bytes} b",
        };
    }

    /// <summary>
    /// Free text as harmless words: letters, digits, spaces, and hyphens only, no destination
    /// markers, at most <see cref="MaxTextLength"/> characters.
    /// </summary>
    private static string? CleanText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            cleaned.Append(char.IsLetterOrDigit(character) || character == '-' ? character : ' ');
        }

        var words = cleaned.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !DestinationMarkers.Contains(word))
            .ToArray();
        if (words.Length == 0)
        {
            return null;
        }

        var joined = string.Join(' ', words);
        return joined.Length > MaxTextLength ? joined[..MaxTextLength].TrimEnd() : joined;
    }

    /// <summary>A destination is one plain folder name: no separators, drive, traversal, marker word, or trailing dot.</summary>
    private static bool TryCleanDestination(string destination, out string folder)
    {
        folder = destination.Trim();
        if (folder.Length is 0 or > 60
            || folder.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) >= 0
            || folder is "." or ".."
            || folder.EndsWith('.')
            || folder.Any(char.IsControl)
            || folder.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(DestinationMarkers.Contains))
        {
            return false;
        }

        return true;
    }

    private static AiSentenceReadingResult Refused(string problem) => new(null, problem);

    private sealed record SearchAnswer(
        [property: JsonPropertyName("schemaVersion")] string? SchemaVersion,
        [property: JsonPropertyName("endings")] IReadOnlyList<string>? Endings,
        [property: JsonPropertyName("categories")] IReadOnlyList<string>? Categories,
        [property: JsonPropertyName("largerThanBytes")] long? LargerThanBytes,
        [property: JsonPropertyName("smallerThanBytes")] long? SmallerThanBytes,
        [property: JsonPropertyName("changedInLastDays")] int? ChangedInLastDays,
        [property: JsonPropertyName("text")] string? Text);

    private sealed record QuestionAnswer(
        [property: JsonPropertyName("schemaVersion")] string? SchemaVersion,
        [property: JsonPropertyName("kind")] string? Kind,
        [property: JsonPropertyName("folder")] string? Folder,
        [property: JsonPropertyName("search")] SearchAnswer? Search);

    private sealed record RuleAnswer(
        [property: JsonPropertyName("schemaVersion")] string? SchemaVersion,
        [property: JsonPropertyName("ending")] string? Ending,
        [property: JsonPropertyName("category")] string? Category,
        [property: JsonPropertyName("largerThanBytes")] long? LargerThanBytes,
        [property: JsonPropertyName("smallerThanBytes")] long? SmallerThanBytes,
        [property: JsonPropertyName("olderThanDays")] int? OlderThanDays,
        [property: JsonPropertyName("nameContains")] string? NameContains,
        [property: JsonPropertyName("destination")] string? Destination);
}
