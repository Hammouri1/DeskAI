using System.Globalization;
using DeskAI.Core.Classification;

namespace DeskAI.Core.Rules;

/// <summary>One condition reduced to two plain strings, ready to be stored.</summary>
public sealed record RuleConditionData(string Kind, string Value);

/// <summary>One action reduced to two plain strings, ready to be stored.</summary>
public sealed record RuleActionData(string Kind, string Value);

/// <summary>
/// Turns rules into stored strings and back, without ever widening what a rule can be.
/// </summary>
/// <remarks>
/// <para>
/// Decoding is the dangerous direction. A row in the database is input like any other: it
/// could be corrupt, hand-edited, or written by a future version of DeskAI. So decoding is
/// an explicit switch over known kinds that throws on anything else, rather than a
/// reflection- or type-name-driven deserializer. Nothing read from storage can name a type
/// to construct, and a stored value still has to pass the same constructor checks a typed
/// rule does — a destination that escapes the folder is refused on the way in and on the
/// way out.
/// </para>
/// <para>
/// The kind strings are stored data, so they are fixed. Renaming one would silently orphan
/// every rule a person has already written, exactly as reordering a stored enum would.
/// </para>
/// </remarks>
public static class RuleCodec
{
    // These strings are in the database. They are not display text and must never be
    // "tidied up": a rename orphans every stored rule that used the old spelling.
    private const string NameContains = "name-contains";
    private const string ExtensionIs = "extension-is";
    private const string CategoryIs = "category-is";
    private const string LargerThan = "larger-than";
    private const string SmallerThan = "smaller-than";
    private const string OlderThan = "older-than";
    private const string NewerThan = "newer-than";
    private const string MoveToFolder = "move-to-folder";

    public static RuleConditionData Encode(RuleCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return condition switch
        {
            NameContainsCondition value => new RuleConditionData(NameContains, value.Text),
            ExtensionIsCondition value => new RuleConditionData(ExtensionIs, value.Extension),
            CategoryIsCondition value => new RuleConditionData(CategoryIs, value.Category.ToString()),
            LargerThanCondition value => new RuleConditionData(LargerThan, Number(value.SizeBytes)),
            SmallerThanCondition value => new RuleConditionData(SmallerThan, Number(value.SizeBytes)),
            OlderThanCondition value => new RuleConditionData(OlderThan, Number(value.Age.Ticks)),
            NewerThanCondition value => new RuleConditionData(NewerThan, Number(value.Age.Ticks)),

            // Loud rather than lossy: a condition type added without a stored spelling must
            // fail here, not be written to disk as something that cannot be read back.
            _ => throw new NotSupportedException($"No stored form is defined for {condition.GetType().Name}."),
        };
    }

    public static RuleActionData Encode(RuleAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return action switch
        {
            MoveToFolderAction value => new RuleActionData(MoveToFolder, value.DestinationRelativeDirectory),
            _ => throw new NotSupportedException($"No stored form is defined for {action.GetType().Name}."),
        };
    }

    /// <summary>
    /// Rebuilds a condition from stored strings, refusing anything it does not recognise.
    /// </summary>
    /// <exception cref="FormatException">The stored row is not something DeskAI understands.</exception>
    public static RuleCondition DecodeCondition(RuleConditionData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        try
        {
            return data.Kind switch
            {
                NameContains => new NameContainsCondition(data.Value),
                ExtensionIs => new ExtensionIsCondition(data.Value),
                CategoryIs => new CategoryIsCondition(ParseCategory(data.Value)),
                LargerThan => new LargerThanCondition(ParseNumber(data.Value)),
                SmallerThan => new SmallerThanCondition(ParseNumber(data.Value)),
                OlderThan => new OlderThanCondition(TimeSpan.FromTicks(ParseNumber(data.Value))),
                NewerThan => new NewerThanCondition(TimeSpan.FromTicks(ParseNumber(data.Value))),
                _ => throw new FormatException($"\"{data.Kind}\" is not a condition DeskAI understands."),
            };
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            // The stored value failed the same check a typed rule would have failed. Reject
            // it rather than repair it: a rule nobody wrote is not one to run.
            throw new FormatException($"A stored \"{data.Kind}\" condition is not usable.", exception);
        }
    }

    /// <inheritdoc cref="DecodeCondition"/>
    public static RuleAction DecodeAction(RuleActionData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        try
        {
            return data.Kind switch
            {
                MoveToFolder => new MoveToFolderAction(data.Value),
                _ => throw new FormatException($"\"{data.Kind}\" is not an action DeskAI understands."),
            };
        }
        catch (ArgumentException exception)
        {
            throw new FormatException("A stored destination is not usable.", exception);
        }
    }

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static long ParseNumber(string value) => long.TryParse(
        value,
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out var parsed)
        ? parsed
        : throw new FormatException($"\"{value}\" is not a stored number.");

    private static FileCategory ParseCategory(string value) =>
        Enum.TryParse<FileCategory>(value, ignoreCase: false, out var category)
            ? category
            : throw new FormatException($"\"{value}\" is not a category DeskAI knows.");
}
