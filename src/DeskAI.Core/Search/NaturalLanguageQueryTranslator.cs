using System.Globalization;
using System.Text.RegularExpressions;
using DeskAI.Core.Classification;

namespace DeskAI.Core.Search;

/// <summary>
/// Turns a short typed phrase such as "big videos from last month" into a validated
/// <see cref="SearchQuery"/>, together with a chip for every part it understood.
/// </summary>
/// <remarks>
/// <para>
/// This is deterministic and local. No AI is involved, and none is needed: the project's
/// rule is that ordinary cases are handled by conventional processing first, with AI
/// reserved for genuinely uncertain ones. A fixed vocabulary is also auditable, which a
/// model's reading of the same sentence is not.
/// </para>
/// <para>
/// The translator is pure. The current moment is an argument rather than a call to
/// <c>DateTimeOffset.UtcNow</c>, so "last month" means the same thing in a test as it does
/// in the app, and the same phrase always produces the same query.
/// </para>
/// <para>
/// Understanding nothing is reported honestly through
/// <see cref="QueryTranslation.UnderstoodAnything"/> rather than by returning a query with
/// no filters, which would list the whole folder and look like a successful search.
/// </para>
/// </remarks>
public sealed partial class NaturalLanguageQueryTranslator
{
    /// <summary>Bounds the phrase so a pasted document cannot become a query.</summary>
    public const int MaxInputLength = 256;

    /// <summary>
    /// What a vague "big" means. Stated as a constant, and repeated in the chip label, so
    /// the person sees the actual number instead of trusting a hidden threshold.
    /// </summary>
    public const long LargeFileThresholdBytes = 100L * 1024 * 1024;

    /// <summary>What a vague "small" means. Shown in the chip label for the same reason.</summary>
    public const long SmallFileThresholdBytes = 1L * 1024 * 1024;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private static readonly (string Pattern, FileCategory Category, string Label)[] CategoryWords =
    [
        // Screenshots first: they are a narrower reading than plain images.
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
    /// Words that carry no search meaning on their own. Removing them keeps "videos from
    /// last month" from searching for the word "from".
    /// </summary>
    private static readonly HashSet<string> IgnoredWords = new(StringComparer.Ordinal)
    {
        "a", "all", "an", "and", "any", "are", "at", "changed", "created", "file", "files",
        "find", "for", "from", "get", "i", "in", "is", "made", "me", "modified", "my",
        "need", "of", "on", "or", "please", "search", "show", "some", "that", "the", "them",
        "these", "this", "those", "want", "was", "were", "with",
        "about", "contain", "containing", "contains", "has", "have", "inside", "says", "text",
        "word", "words", "written", "whose",
    };

    /// <summary>
    /// Reads <paramref name="text"/> into a query. <paramref name="nowUtc"/> anchors every
    /// relative date so the result is reproducible.
    /// </summary>
    /// <exception cref="ArgumentException">The phrase is longer than <see cref="MaxInputLength"/>.</exception>
    public static QueryTranslation Translate(
        string? text,
        DateTimeOffset nowUtc,
        int limit = SearchQuery.DefaultLimit)
    {
        if (text is not null && text.Length > MaxInputLength)
        {
            throw new ArgumentException(
                $"A search phrase cannot be longer than {MaxInputLength} characters.",
                nameof(text));
        }

        var chips = new List<QueryChip>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new QueryTranslation(new SearchQuery(limit: limit), chips);
        }

        // Matched phrases are blanked out as they are consumed, so a word can only be read
        // once and leftovers are exactly the words nothing claimed.
        var remaining = text.ToLowerInvariant();

        var endings = TakeFileEndings(ref remaining, chips);
        TakeNamedFileEndings(ref remaining, endings, chips);
        var categories = TakeCategories(ref remaining, chips);
        var (minSize, maxSize) = TakeSizes(ref remaining, chips);
        var changedAfter = TakeChangedAfter(ref remaining, chips, nowUtc);
        var freeText = TakeFreeText(remaining, chips);

        var query = new SearchQuery(
            pathContains: freeText,
            extensions: endings,
            categories: categories,
            minSizeBytes: minSize,
            maxSizeBytes: maxSize,
            modifiedAfterUtc: changedAfter,
            limit: limit);

        return new QueryTranslation(query, chips);
    }

    private static List<string> TakeFileEndings(ref string remaining, List<QueryChip> chips)
    {
        var endings = new List<string>();
        while (true)
        {
            var match = FileEndingPattern().Match(remaining);
            if (!match.Success)
            {
                return endings;
            }

            var ending = "." + match.Groups[1].Value;
            if (!endings.Contains(ending, StringComparer.Ordinal))
            {
                endings.Add(ending);
                chips.Add(new QueryChip(QueryFilter.FileEnding, $"Ends with {ending}"));
            }

            Blank(ref remaining, match);
        }
    }

    private static void TakeNamedFileEndings(ref string remaining, List<string> endings, List<QueryChip> chips)
    {
        foreach (var (pattern, ending) in new[]
        {
            (@"\bpdfs?\b", ".pdf"),
            (@"\b(?:word documents?|docx)\b", ".docx"),
            (@"\b(?:excel (?:files?|sheets?|spreadsheets?)|xlsx)\b", ".xlsx"),
            (@"\b(?:power\s?point(?:\s+(?:files?|presentations?))?|pptx)\b", ".pptx"),
        })
        {
            if (TryTake(ref remaining, pattern) && !endings.Contains(ending, StringComparer.Ordinal))
            {
                endings.Add(ending);
                chips.Add(new QueryChip(QueryFilter.FileEnding, $"Ends with {ending}"));
            }
        }
    }

    private static List<FileCategory> TakeCategories(ref string remaining, List<QueryChip> chips)
    {
        var categories = new List<FileCategory>();
        foreach (var (pattern, category, label) in CategoryWords)
        {
            if (TryTake(ref remaining, pattern) && !categories.Contains(category))
            {
                categories.Add(category);
                chips.Add(new QueryChip(QueryFilter.Category, label));
            }
        }

        return categories;
    }

    private static (long? Minimum, long? Maximum) TakeSizes(ref string remaining, List<QueryChip> chips)
    {
        long? minimum = null;
        long? maximum = null;

        if (TryTakeMatch(ref remaining, LargerThanPattern(), out var larger)
            && TryReadSize(larger, out var largerBytes))
        {
            minimum = largerBytes;
            chips.Add(new QueryChip(QueryFilter.MinimumSize, $"Larger than {Describe(largerBytes)}"));
        }

        if (TryTakeMatch(ref remaining, SmallerThanPattern(), out var smaller)
            && TryReadSize(smaller, out var smallerBytes))
        {
            maximum = smallerBytes;
            chips.Add(new QueryChip(QueryFilter.MaximumSize, $"Smaller than {Describe(smallerBytes)}"));
        }

        // A vague word never overrides an explicit number, and never contradicts one. If
        // someone types "big files under 5 MB" the number is what they actually said, so
        // the vague half is dropped rather than making the query impossible.
        var saidBig = TryTake(ref remaining, @"\b(big|large|huge)\b");
        var saidSmall = TryTake(ref remaining, @"\b(small|tiny)\b");

        if (saidBig && minimum is null && maximum is not < LargeFileThresholdBytes)
        {
            minimum = LargeFileThresholdBytes;
            chips.Add(new QueryChip(
                QueryFilter.MinimumSize,
                $"Larger than {Describe(LargeFileThresholdBytes)}"));
        }

        if (saidSmall && maximum is null && minimum is not > SmallFileThresholdBytes)
        {
            maximum = SmallFileThresholdBytes;
            chips.Add(new QueryChip(
                QueryFilter.MaximumSize,
                $"Smaller than {Describe(SmallFileThresholdBytes)}"));
        }

        return (minimum, maximum);
    }

    private static DateTimeOffset? TakeChangedAfter(
        ref string remaining,
        List<QueryChip> chips,
        DateTimeOffset nowUtc)
    {
        var startOfToday = new DateTimeOffset(nowUtc.UtcDateTime.Date, TimeSpan.Zero);

        if (TryTakeMatch(ref remaining, LastDaysPattern(), out var lastDays)
            && int.TryParse(lastDays.Groups[1].Value, CultureInfo.InvariantCulture, out var days)
            && days is > 0 and <= 3650)
        {
            return Add(chips, nowUtc.AddDays(-days), $"Changed in the last {days} days");
        }

        if (TryTake(ref remaining, @"\btoday\b"))
        {
            return Add(chips, startOfToday, "Changed today");
        }

        if (TryTake(ref remaining, @"\byesterday\b"))
        {
            return Add(chips, startOfToday.AddDays(-1), "Changed since yesterday");
        }

        if (TryTake(ref remaining, @"\b(this|last|past)\s+week\b"))
        {
            return Add(chips, nowUtc.AddDays(-7), "Changed in the last 7 days");
        }

        if (TryTake(ref remaining, @"\b(this|last|past)\s+month\b"))
        {
            return Add(chips, nowUtc.AddMonths(-1), "Changed in the last month");
        }

        return TryTake(ref remaining, @"\b(this|last|past)\s+year\b")
            ? Add(chips, nowUtc.AddYears(-1), "Changed in the last year")
            : null;

        static DateTimeOffset Add(List<QueryChip> chips, DateTimeOffset moment, string label)
        {
            chips.Add(new QueryChip(QueryFilter.ChangedAfter, label));
            return moment;
        }
    }

    private static string? TakeFreeText(string remaining, List<QueryChip> chips)
    {
        var words = remaining
            .Split([' ', '\t', '\n', '\r', ',', ';', '"', '\''], StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !IgnoredWords.Contains(word))
            .ToArray();

        if (words.Length == 0)
        {
            return null;
        }

        var text = string.Join(' ', words);
        if (text.Length > SearchQuery.MaxTextLength)
        {
            text = text[..SearchQuery.MaxTextLength];
        }

        chips.Add(new QueryChip(QueryFilter.Text, $"Look for \"{text}\""));
        return text;
    }

    private static bool TryReadSize(Match match, out long bytes)
    {
        bytes = 0;
        if (!double.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var amount)
            || amount <= 0)
        {
            return false;
        }

        var multiplier = match.Groups[2].Value switch
        {
            "kb" => 1024L,
            "mb" => 1024L * 1024,
            "gb" => 1024L * 1024 * 1024,
            _ => 1L,
        };

        var scaled = amount * multiplier;
        if (scaled is <= 0 or > long.MaxValue)
        {
            return false;
        }

        bytes = (long)scaled;
        return true;
    }

    /// <summary>Renders a byte count the way the person most likely typed it.</summary>
    private static string Describe(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.##} GB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):0.##} MB",
        >= 1024 => $"{bytes / 1024d:0.##} KB",
        _ => $"{bytes} bytes",
    };

    private static bool TryTake(ref string remaining, string pattern) =>
        TryTakeMatch(
            ref remaining,
            new Regex(pattern, RegexOptions.CultureInvariant, MatchTimeout),
            out _);

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

    /// <summary>
    /// Replaces a consumed span with spaces rather than deleting it, so the words around it
    /// keep their boundaries and cannot accidentally join into a new word.
    /// </summary>
    private static void Blank(ref string remaining, Match match) =>
        remaining = string.Concat(
            remaining.AsSpan(0, match.Index),
            new string(' ', match.Length),
            remaining.AsSpan(match.Index + match.Length));

    [GeneratedRegex(@"(?<![\w.])\.([a-z0-9]{1,10})\b", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex FileEndingPattern();

    [GeneratedRegex(
        @"\b(?:over|above|more\s+than|larger\s+than|bigger\s+than|greater\s+than)\s+(\d+(?:\.\d+)?)\s*(gb|mb|kb|b)\b",
        RegexOptions.CultureInvariant,
        1000)]
    private static partial Regex LargerThanPattern();

    [GeneratedRegex(
        @"\b(?:under|below|less\s+than|smaller\s+than)\s+(\d+(?:\.\d+)?)\s*(gb|mb|kb|b)\b",
        RegexOptions.CultureInvariant,
        1000)]
    private static partial Regex SmallerThanPattern();

    [GeneratedRegex(@"\b(?:last|past)\s+(\d{1,4})\s+days?\b", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex LastDaysPattern();
}
