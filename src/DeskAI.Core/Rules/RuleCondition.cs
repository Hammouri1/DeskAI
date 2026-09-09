using DeskAI.Core.Classification;

namespace DeskAI.Core.Rules;

/// <summary>
/// One test a rule applies to a file.
/// </summary>
/// <remarks>
/// <para>
/// A closed set, not an expression language. Every condition DeskAI understands is a type in
/// this file, so what a rule can possibly test is readable in one place and cannot be
/// extended by anything a person types or a model returns. A rule assembled from untrusted
/// text can only ever be a combination of these.
/// </para>
/// <para>
/// Every condition can describe itself, because a rule a person cannot read back in plain
/// words is one they cannot safely approve.
/// </para>
/// </remarks>
public abstract record RuleCondition
{
    /// <summary>The longest piece of text a condition may hold.</summary>
    /// <remarks>
    /// Rules are typed by people and may one day be drafted from a sentence. A bound keeps
    /// an absurd condition out of the database and out of the UI.
    /// </remarks>
    public const int MaxTextLength = 120;

    public abstract bool Matches(RuleSubject subject, DateTimeOffset nowUtc);

    /// <summary>How this reads to a person, for the rule summary and the simulator.</summary>
    public abstract string Describe();
}

/// <summary>Matches when the file name contains some text, ignoring capitalisation.</summary>
public sealed record NameContainsCondition : RuleCondition
{
    public NameContainsCondition(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (text.Length > MaxTextLength)
        {
            throw new ArgumentException($"Text must be {MaxTextLength} characters or fewer.", nameof(text));
        }

        Text = text;
    }

    public string Text { get; }

    public override bool Matches(RuleSubject subject, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return subject.Name.Contains(Text, StringComparison.OrdinalIgnoreCase);
    }

    public override string Describe() => $"the name contains \"{Text}\"";
}

/// <summary>Matches one exact file ending.</summary>
public sealed record ExtensionIsCondition : RuleCondition
{
    /// <summary>Characters that would make an ending a path or a pattern instead.</summary>
    private static readonly System.Buffers.SearchValues<char> NotInAnEnding =
        System.Buffers.SearchValues.Create(['.', '\\', '/', '*', '?', ':']);

    public ExtensionIsCondition(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        var normalized = extension.StartsWith('.') ? extension : "." + extension;

        // A file ending, not a pattern and not a path. Accepting either would turn a
        // deliberately narrow condition into something with reach nobody reviewed.
        if (normalized.Length > MaxTextLength
            || normalized.AsSpan(1).ContainsAny(NotInAnEnding)
            || normalized.Length == 1)
        {
            throw new ArgumentException("A file ending looks like \".txt\".", nameof(extension));
        }

        Extension = normalized;
    }

    public string Extension { get; }

    public override bool Matches(RuleSubject subject, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return string.Equals(subject.Extension, Extension, StringComparison.OrdinalIgnoreCase);
    }

    public override string Describe() => $"it is a {Extension} file";
}

/// <summary>Matches files DeskAI classified into one category.</summary>
public sealed record CategoryIsCondition(FileCategory Category) : RuleCondition
{
    public override bool Matches(RuleSubject subject, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return subject.Category == Category;
    }

    public override string Describe() => $"it is filed under {Category}";
}

/// <summary>Matches files strictly larger than a size.</summary>
public sealed record LargerThanCondition : RuleCondition
{
    public LargerThanCondition(long sizeBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);
        SizeBytes = sizeBytes;
    }

    public long SizeBytes { get; }

    public override bool Matches(RuleSubject subject, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return subject.SizeBytes > SizeBytes;
    }

    public override string Describe() => $"it is bigger than {SizeBytes} bytes";
}

/// <summary>Matches files strictly smaller than a size.</summary>
public sealed record SmallerThanCondition : RuleCondition
{
    public SmallerThanCondition(long sizeBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);
        SizeBytes = sizeBytes;
    }

    public long SizeBytes { get; }

    public override bool Matches(RuleSubject subject, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return subject.SizeBytes < SizeBytes;
    }

    public override string Describe() => $"it is smaller than {SizeBytes} bytes";
}

/// <summary>
/// Matches files that have not changed for longer than an age.
/// </summary>
/// <remarks>
/// The moment is passed in rather than read from the clock, so the same rule against the
/// same files gives the same answer in a simulation as it does in a run. A rule whose
/// meaning depends on when it happens to be evaluated cannot be simulated honestly.
/// </remarks>
public sealed record OlderThanCondition : RuleCondition
{
    public OlderThanCondition(TimeSpan age)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(age, TimeSpan.Zero);
        Age = age;
    }

    public TimeSpan Age { get; }

    public override bool Matches(RuleSubject subject, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return nowUtc - subject.ModifiedAtUtc > Age;
    }

    public override string Describe() => $"it has not changed in {DescribeAge(Age)}";

    internal static string DescribeAge(TimeSpan age) => age.TotalDays switch
    {
        >= 365 => $"{age.TotalDays / 365:0.#} year(s)",
        >= 30 => $"{age.TotalDays / 30:0.#} month(s)",
        >= 1 => $"{age.TotalDays:0.#} day(s)",
        _ => $"{age.TotalHours:0.#} hour(s)",
    };
}

/// <inheritdoc cref="OlderThanCondition"/>
public sealed record NewerThanCondition : RuleCondition
{
    public NewerThanCondition(TimeSpan age)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(age, TimeSpan.Zero);
        Age = age;
    }

    public TimeSpan Age { get; }

    public override bool Matches(RuleSubject subject, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return nowUtc - subject.ModifiedAtUtc <= Age;
    }

    public override string Describe() => $"it changed in the last {OlderThanCondition.DescribeAge(Age)}";
}
