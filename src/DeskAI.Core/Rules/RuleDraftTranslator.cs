using System.Globalization;
using System.Text.RegularExpressions;
using DeskAI.Core.Classification;

namespace DeskAI.Core.Rules;

/// <summary>One understood piece of a typed sentence, written for a person to read back.</summary>
public sealed record RuleDraftChip(string Label);

/// <summary>
/// A suggested rule, for review. Not a rule.
/// </summary>
/// <remarks>
/// <para>
/// A draft is filled into the form so a person can see, change, and agree to it before
/// anything is saved. It is deliberately not an <see cref="AutomationRule"/>: nothing here
/// has been through the checks a real rule passes, and nothing should be able to act on a
/// sentence DeskAI merely thought it understood.
/// </para>
/// <para>
/// <see cref="DestinationProblem"/> carries the reason a destination was refused instead of
/// throwing, because "I could not use that folder name" is something to show someone, not an
/// error to swallow.
/// </para>
/// </remarks>
public sealed record RuleDraft(
    IReadOnlyList<RuleCondition> Conditions,
    MoveToFolderAction? Action,
    IReadOnlyList<RuleDraftChip> Chips,
    string? DestinationProblem)
{
    public static RuleDraft Empty { get; } = new([], null, [], null);

    public bool UnderstoodAnything => Chips.Count > 0;

    /// <summary>True when the draft has both something to look for and somewhere to put it.</summary>
    public bool IsComplete => Conditions.Count > 0 && Action is not null;
}

/// <summary>
/// Reads a sentence such as "move invoices to Documents" into a suggested rule.
/// </summary>
/// <remarks>
/// <para>
/// Deterministic and local, with a fixed vocabulary and no AI — the same choice made for
/// search, for the same reasons. Ordinary phrasings are handled conventionally, the reading
/// is auditable in a way a model's is not, and the same sentence always produces the same
/// draft. When AI drafting is added later it must produce this same <see cref="RuleDraft"/>
/// through the same validated path, so it gains no new reach.
/// </para>
/// <para>
/// Every part understood becomes a chip, so the reading is visible and correctable. A
/// sentence understood only in part is still offered as a draft, because half a rule someone
/// can finish is more useful than a refusal — but it is never saved for them.
/// </para>
/// </remarks>
public static partial class RuleDraftTranslator
{
    /// <summary>Bounds the sentence so a pasted document cannot become a rule.</summary>
    public const int MaxInputLength = 256;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Words that mean a file type when someone says "pdf files".
    /// </summary>
    /// <remarks>
    /// Only used for the bare form. It exists so "invoice files" is read as a name to look
    /// for rather than an ending of ".invoice". It does not limit what a rule can hold: an
    /// explicit ".invoice" is still accepted, and the stored rule is the same either way.
    /// </remarks>
    private static readonly HashSet<string> KnownEndings = new(StringComparer.OrdinalIgnoreCase)
    {
        "pdf", "doc", "docx", "txt", "md", "rtf", "odt", "epub", "tex",
        "xls", "xlsx", "csv", "tsv", "ods", "ppt", "pptx", "odp",
        "jpg", "jpeg", "png", "gif", "bmp", "webp", "svg", "heic", "tif", "tiff", "psd",
        "mp3", "wav", "flac", "m4a", "aac", "ogg", "wma",
        "mp4", "mov", "mkv", "avi", "webm", "wmv", "m4v",
        "zip", "7z", "rar", "tar", "gz", "iso",
        "exe", "msi", "msix", "log", "json", "xml", "yaml", "yml", "ini", "csv",
    };

    private static readonly (string Pattern, FileCategory Category, string Label)[] CategoryWords =
    [
        // Screenshots before images: it is the narrower reading of the same thing.
        (@"\bscreen\s?shots?\b", FileCategory.Screenshots, "Screenshots"),
        (@"\b(photos?|pictures?|images?)\b", FileCategory.Images, "Photos"),
        (@"\b(videos?|movies?|clips?)\b", FileCategory.Videos, "Videos"),
        (@"\b(music|songs?|audio)\b", FileCategory.Audio, "Music"),
        (@"\bspreadsheets?\b", FileCategory.Spreadsheets, "Spreadsheets"),
        (@"\b(presentations?|slides)\b", FileCategory.Presentations, "Presentations"),
        (@"\b(archives?|zips?)\b", FileCategory.Archives, "Archives"),
        (@"\b(installers?|setups?)\b", FileCategory.Installers, "Installers"),
        (@"\bdocuments?\b", FileCategory.Documents, "Documents"),
    ];

    /// <summary>
    /// Words that say what to do rather than what to look for.
    /// </summary>
    /// <remarks>
    /// Without this, "move invoices" would look for files with "move" in the name.
    /// </remarks>
    private static readonly HashSet<string> IgnoredWords = new(StringComparer.Ordinal)
    {
        "a", "all", "always", "an", "and", "any", "anything", "are", "called", "everything",
        "file", "files", "filed", "for", "get", "i", "is", "keep", "me", "move", "my",
        "named", "of", "please", "put", "send", "should", "sort", "that", "the", "them",
        "these", "they", "this", "those", "want", "which", "with",
    };

    /// <summary>
    /// Reads <paramref name="sentence"/> into a draft rule for review.
    /// </summary>
    /// <exception cref="ArgumentException">The sentence is longer than <see cref="MaxInputLength"/>.</exception>
    public static RuleDraft Draft(string? sentence)
    {
        if (sentence is not null && sentence.Length > MaxInputLength)
        {
            throw new ArgumentException(
                $"A sentence cannot be longer than {MaxInputLength} characters.",
                nameof(sentence));
        }

        if (string.IsNullOrWhiteSpace(sentence))
        {
            return RuleDraft.Empty;
        }

        var chips = new List<RuleDraftChip>();
        var (subject, destinationText) = SplitOnDestination(sentence);

        // Matched phrases are blanked as they are consumed, so a word is read once and the
        // leftovers are exactly what nothing else claimed.
        var remaining = subject.ToLowerInvariant();
        var conditions = new List<RuleCondition>();

        TakeFileEnding(ref remaining, conditions, chips);
        TakeCategory(ref remaining, conditions, chips);
        TakeSizes(ref remaining, conditions, chips);
        TakeAge(ref remaining, conditions, chips);
        TakeNameText(remaining, conditions, chips);

        var (action, problem) = ReadDestination(destinationText, chips);
        return new RuleDraft(conditions.AsReadOnly(), action, chips.AsReadOnly(), problem);
    }

    /// <summary>
    /// Splits "move invoices to Documents" into what to look for and where to put it.
    /// </summary>
    /// <remarks>
    /// "to" and "into" are looked for first because they almost always mean a destination.
    /// "in" is only used when neither appears, since "files in 2026" is far more often part
    /// of a name than a folder. The original text is sliced, not the lowercased copy, so a
    /// folder keeps the capitals someone typed.
    /// </remarks>
    private static (string Subject, string? Destination) SplitOnDestination(string sentence)
    {
        var lower = sentence.ToLowerInvariant();
        foreach (var marker in new[] { " into ", " to ", " in " })
        {
            var index = lower.LastIndexOf(marker, StringComparison.Ordinal);
            if (index >= 0)
            {
                var destination = sentence[(index + marker.Length)..].Trim().TrimEnd('.');
                return (sentence[..index], destination.Length == 0 ? null : destination);
            }
        }

        return (sentence, null);
    }

    private static (MoveToFolderAction? Action, string? Problem) ReadDestination(
        string? destination,
        List<RuleDraftChip> chips)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            return (null, null);
        }

        try
        {
            var action = new MoveToFolderAction(destination);
            chips.Add(new RuleDraftChip($"Move into {action.DestinationRelativeDirectory}"));
            return (action, null);
        }
        catch (ArgumentException)
        {
            // Said, not thrown: a folder name DeskAI cannot use is something to show the
            // person so they can correct it.
            return (null, $"\"{destination}\" cannot be used as a folder inside a connected folder.");
        }
    }

    /// <summary>
    /// Reads a file ending, from ".pdf" or from "pdf files".
    /// </summary>
    /// <remarks>
    /// The bare "<c>word</c> files" form only counts when the word is actually a file type
    /// DeskAI knows. Without that check, "invoice files" reads as an ending of ".invoice" —
    /// a rule that would match nothing and that nobody meant to write. An explicit dot is
    /// always believed, so someone with an unusual ending can still say ".invoice".
    /// </remarks>
    private static void TakeFileEnding(
        ref string remaining,
        List<RuleCondition> conditions,
        List<RuleDraftChip> chips)
    {
        var match = DottedEndingPattern().Match(remaining);
        if (!match.Success)
        {
            match = SpokenEndingPattern().Match(remaining);
            if (!match.Success || !KnownEndings.Contains(match.Groups[1].Value))
            {
                return;
            }
        }

        var ending = "." + match.Groups[1].Value;
        try
        {
            conditions.Add(new ExtensionIsCondition(ending));
            chips.Add(new RuleDraftChip($"Ends with {ending}"));
        }
        catch (ArgumentException)
        {
            return;
        }

        Blank(ref remaining, match);
    }

    private static void TakeCategory(
        ref string remaining,
        List<RuleCondition> conditions,
        List<RuleDraftChip> chips)
    {
        foreach (var (pattern, category, label) in CategoryWords)
        {
            if (TryTake(ref remaining, pattern))
            {
                conditions.Add(new CategoryIsCondition(category));
                chips.Add(new RuleDraftChip($"Filed under {label}"));
                return;
            }
        }
    }

    private static void TakeSizes(
        ref string remaining,
        List<RuleCondition> conditions,
        List<RuleDraftChip> chips)
    {
        if (TryTakeMatch(ref remaining, LargerThanPattern(), out var larger)
            && TryReadSize(larger, out var largerBytes))
        {
            conditions.Add(new LargerThanCondition(largerBytes));
            chips.Add(new RuleDraftChip($"Bigger than {Describe(largerBytes)}"));
        }

        if (TryTakeMatch(ref remaining, SmallerThanPattern(), out var smaller)
            && TryReadSize(smaller, out var smallerBytes))
        {
            conditions.Add(new SmallerThanCondition(smallerBytes));
            chips.Add(new RuleDraftChip($"Smaller than {Describe(smallerBytes)}"));
        }
    }

    private static void TakeAge(
        ref string remaining,
        List<RuleCondition> conditions,
        List<RuleDraftChip> chips)
    {
        if (!TryTakeMatch(ref remaining, OlderThanPattern(), out var match)
            || !int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            || count <= 0)
        {
            return;
        }

        var unit = match.Groups[2].Value;
        var age = unit switch
        {
            "day" or "days" => TimeSpan.FromDays(count),
            "week" or "weeks" => TimeSpan.FromDays(count * 7),
            "month" or "months" => TimeSpan.FromDays(count * 30),
            _ => TimeSpan.FromDays(count * 365),
        };

        conditions.Add(new OlderThanCondition(age));
        chips.Add(new RuleDraftChip($"Not changed in {count} {unit}"));
    }

    /// <summary>
    /// Whatever is left over becomes the text to look for in the name.
    /// </summary>
    /// <remarks>
    /// A trailing "s" is dropped from a single leftover word, so "move invoices" finds
    /// "invoice-march.pdf". This is a guess, which is exactly why it is shown as a chip the
    /// person can correct rather than applied silently.
    /// </remarks>
    private static void TakeNameText(
        string remaining,
        List<RuleCondition> conditions,
        List<RuleDraftChip> chips)
    {
        var words = remaining
            .Split([' ', '\t', ',', '"', '\''], StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 1 && !IgnoredWords.Contains(word))
            .ToArray();

        if (words.Length == 0)
        {
            return;
        }

        var text = words.Length == 1 ? Singularize(words[0]) : string.Join(' ', words);
        if (text.Length < 2 || conditions.Count >= AutomationRule.MaxConditions)
        {
            return;
        }

        conditions.Add(new NameContainsCondition(text));
        chips.Add(new RuleDraftChip($"Name contains \"{text}\""));
    }

    private static string Singularize(string word) =>
        word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal)
            ? word[..^1]
            : word;

    private static bool TryReadSize(Match match, out long bytes)
    {
        bytes = 0;
        if (!double.TryParse(
            match.Groups[1].Value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var amount) || amount <= 0)
        {
            return false;
        }

        var multiplier = match.Groups[2].Value switch
        {
            "kb" => 1024d,
            "mb" => 1024d * 1024,
            "gb" => 1024d * 1024 * 1024,
            _ => 1d,
        };

        var total = amount * multiplier;
        if (total is <= 0 or > long.MaxValue)
        {
            return false;
        }

        bytes = (long)total;
        return true;
    }

    private static string Describe(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.#} GB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):0.#} MB",
        >= 1024 => $"{bytes / 1024d:0.#} KB",
        _ => $"{bytes} bytes",
    };

    private static bool TryTake(ref string remaining, string pattern)
    {
        var match = Regex.Match(remaining, pattern, RegexOptions.IgnoreCase, MatchTimeout);
        if (!match.Success)
        {
            return false;
        }

        Blank(ref remaining, match);
        return true;
    }

    private static bool TryTakeMatch(ref string remaining, Regex pattern, out Match match)
    {
        match = pattern.Match(remaining);
        if (!match.Success)
        {
            return false;
        }

        Blank(ref remaining, match);
        return true;
    }

    private static void Blank(ref string remaining, Match match) =>
        remaining = remaining.Remove(match.Index, match.Length).Insert(match.Index, new string(' ', match.Length));

    [GeneratedRegex(@"\.([a-z0-9]{1,8})\b", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex DottedEndingPattern();

    [GeneratedRegex(@"\b([a-z0-9]{1,8})\s?files?\b", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex SpokenEndingPattern();

    [GeneratedRegex(@"\b(?:bigger|larger|more)\s+than\s+(\d+(?:\.\d+)?)\s*(kb|mb|gb|bytes?)\b", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex LargerThanPattern();

    [GeneratedRegex(@"\b(?:smaller|less)\s+than\s+(\d+(?:\.\d+)?)\s*(kb|mb|gb|bytes?)\b", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex SmallerThanPattern();

    [GeneratedRegex(@"\b(?:older\s+than|not\s+changed\s+in|unchanged\s+for)\s+(\d+)\s*(days?|weeks?|months?|years?)\b", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex OlderThanPattern();
}
