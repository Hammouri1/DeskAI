using DeskAI.Core.Classification;
using DeskAI.Core.Rules;

namespace DeskAI.Core.Tests;

/// <summary>
/// Decoding is the dangerous direction: a stored row is input like any other, and could be
/// corrupt, hand-edited, or written by a later version of DeskAI.
/// </summary>
public sealed class RuleCodecTests
{
    public static TheoryData<RuleCondition> EveryConditionKind() =>
    [
        new NameContainsCondition("invoice"),
        new ExtensionIsCondition(".pdf"),
        new CategoryIsCondition(FileCategory.Images),
        new LargerThanCondition(10_000),
        new SmallerThanCondition(500),
        new OlderThanCondition(TimeSpan.FromDays(180)),
        new NewerThanCondition(TimeSpan.FromHours(6)),
    ];

    [Theory]
    [MemberData(nameof(EveryConditionKind))]
    public void EveryConditionSurvivesBeingStoredAndReadBack(RuleCondition condition)
    {
        var restored = RuleCodec.DecodeCondition(RuleCodec.Encode(condition));

        Assert.Equal(condition, restored);
    }

    [Fact]
    public void AnActionSurvivesBeingStoredAndReadBack()
    {
        var action = new MoveToFolderAction(@"Documents\Invoices");

        Assert.Equal(action, RuleCodec.DecodeAction(RuleCodec.Encode(action)));
    }

    /// <summary>
    /// These strings are in the database. Renaming one would silently orphan every rule a
    /// person has already written, exactly as reordering a stored enum would.
    /// </summary>
    [Fact]
    public void StoredKindNamesNeverChange()
    {
        Assert.Equal("name-contains", RuleCodec.Encode(new NameContainsCondition("x")).Kind);
        Assert.Equal("extension-is", RuleCodec.Encode(new ExtensionIsCondition(".pdf")).Kind);
        Assert.Equal("category-is", RuleCodec.Encode(new CategoryIsCondition(FileCategory.Images)).Kind);
        Assert.Equal("larger-than", RuleCodec.Encode(new LargerThanCondition(1)).Kind);
        Assert.Equal("smaller-than", RuleCodec.Encode(new SmallerThanCondition(1)).Kind);
        Assert.Equal("older-than", RuleCodec.Encode(new OlderThanCondition(TimeSpan.FromDays(1))).Kind);
        Assert.Equal("newer-than", RuleCodec.Encode(new NewerThanCondition(TimeSpan.FromDays(1))).Kind);
        Assert.Equal("move-to-folder", RuleCodec.Encode(new MoveToFolderAction("Sorted")).Kind);
    }

    /// <summary>
    /// Nothing read from storage may name a type to construct. An unknown kind is refused
    /// rather than guessed at or ignored.
    /// </summary>
    [Theory]
    [InlineData("run-program")]
    [InlineData("delete-file")]
    [InlineData("System.Diagnostics.Process")]
    [InlineData("")]
    public void AnUnknownConditionKindIsRefused(string kind)
    {
        Assert.Throws<FormatException>(() =>
            RuleCodec.DecodeCondition(new RuleConditionData(kind, "anything")));
    }

    [Theory]
    [InlineData("delete-file")]
    [InlineData("run-program")]
    public void AnUnknownActionKindIsRefused(string kind)
    {
        Assert.Throws<FormatException>(() => RuleCodec.DecodeAction(new RuleActionData(kind, "anything")));
    }

    /// <summary>
    /// A stored value still faces the same checks a typed rule does. A destination that
    /// escapes the folder is refused coming out of the database, not only going in — which
    /// is what stops a hand-edited row from becoming a rule nobody could have written.
    /// </summary>
    [Theory]
    [InlineData(@"..\..\Windows")]
    [InlineData(@"C:\Windows")]
    [InlineData("Sorted*")]
    public void AStoredDestinationThatEscapesTheFolderIsRefused(string destination)
    {
        Assert.Throws<FormatException>(() =>
            RuleCodec.DecodeAction(new RuleActionData("move-to-folder", destination)));
    }

    [Theory]
    [InlineData("larger-than", "not a number")]
    [InlineData("larger-than", "")]
    [InlineData("category-is", "NotACategory")]
    [InlineData("extension-is", "*.pdf")]
    [InlineData("name-contains", "")]
    public void AStoredValueThatIsNotUsableIsRefused(string kind, string value)
    {
        Assert.Throws<FormatException>(() => RuleCodec.DecodeCondition(new RuleConditionData(kind, value)));
    }
}
