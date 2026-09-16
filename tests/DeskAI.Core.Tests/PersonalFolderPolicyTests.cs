using DeskAI.Core.Abstractions;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Tests;

/// <summary>
/// The owner's rule of 2026-09-16 (ADR 0032): DeskAI connects only a person's own Desktop,
/// Downloads, Documents, and Pictures, or a folder inside one of them. Every path here is
/// made up; nothing asks Windows for a real folder.
/// </summary>
public sealed class PersonalFolderPolicyTests
{
    private static readonly PersonalFolderPolicy Policy = new(new FakeKnownFolders
    {
        Desktop = @"C:\DeskAITests\Someone\Desktop",
        Downloads = @"C:\DeskAITests\Someone\Downloads\",
        Documents = @"C:\DeskAITests\Someone\Documents",
        Pictures = @"C:\DeskAITests\Someone\Pictures",
    });

    [Theory]
    [InlineData(@"C:\DeskAITests\Someone\Desktop")]
    [InlineData(@"C:\DeskAITests\Someone\Desktop\")]
    [InlineData(@"c:\deskaitests\someone\DOWNLOADS")]
    [InlineData(@"C:\DeskAITests\Someone\Documents\Uni\Year 2")]
    [InlineData(@"C:\DeskAITests\Someone\Pictures\2026")]
    public void A_personal_folder_or_a_folder_inside_one_may_be_connected(string path)
    {
        Assert.Null(Policy.Refuse(path));
        Assert.NotNull(Policy.Containing(path));
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\DeskAITests")]
    [InlineData(@"C:\DeskAITests\Someone")]
    [InlineData(@"C:\DeskAITests\Someone\Desktop2")]
    [InlineData(@"C:\DeskAITests\Someone\Videos")]
    [InlineData(@"C:\DeskAITests\Someone\Desktop\..\Videos")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"D:\Work")]
    public void Anything_else_is_refused_in_plain_words(string path)
    {
        Assert.Equal(PersonalFolderPolicy.OutsideReason, Policy.Refuse(path));
        Assert.Null(Policy.Containing(path));
    }

    [Fact]
    public void The_list_keeps_the_page_order_and_leaves_out_what_Windows_does_not_have()
    {
        var policy = new PersonalFolderPolicy(new FakeKnownFolders
        {
            Desktop = @"C:\DeskAITests\Someone\Desktop",
            Pictures = @"C:\DeskAITests\Someone\Pictures",
        });

        Assert.Equal(
            [PersonalFolderKind.Desktop, PersonalFolderKind.Pictures],
            policy.List().Select(folder => folder.Kind));
        Assert.Equal(@"C:\DeskAITests\Someone\Desktop", policy.Find(PersonalFolderKind.Desktop)!.Path);
        Assert.Null(policy.Find(PersonalFolderKind.Downloads));
        Assert.Equal(PersonalFolderPolicy.OutsideReason, policy.Refuse(@"C:\DeskAITests\Someone\Downloads"));
    }

    [Fact]
    public void With_no_personal_folder_known_nothing_can_be_connected_and_the_reason_says_so()
    {
        var policy = new PersonalFolderPolicy(new FakeKnownFolders());

        Assert.Empty(policy.List());
        Assert.Equal(PersonalFolderPolicy.NoneKnownReason, policy.Refuse(@"C:\DeskAITests\Someone\Desktop"));
    }

    [Fact]
    public void A_relative_or_malformed_known_folder_is_ignored_rather_than_trusted()
    {
        var policy = new PersonalFolderPolicy(new FakeKnownFolders { Desktop = "Desktop", Documents = "   " });

        Assert.Empty(policy.List());
    }

    private sealed class FakeKnownFolders : IKnownFolders
    {
        public string? Desktop { get; init; }

        public string? Downloads { get; init; }

        public string? Documents { get; init; }

        public string? Pictures { get; init; }
    }
}
