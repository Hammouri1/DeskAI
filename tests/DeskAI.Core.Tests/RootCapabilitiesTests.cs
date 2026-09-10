using DeskAI.Core.Roots;

namespace DeskAI.Core.Tests;

/// <summary>
/// The permission gate in front of content access. Every case here is a refusal case except
/// where a right was deliberately granted, because the cost of this type being wrong is a
/// person's files being opened without them agreeing to it.
/// </summary>
public sealed class RootCapabilitiesTests
{
    /// <summary>
    /// The whole matrix, written out. A table is worth more than three separate tests here:
    /// a right that appears where it should not is visible at a glance, and adding a scope
    /// without adding its row makes the count assertion below fail.
    /// </summary>
    [Theory]
    [InlineData(RootAuthorizationScope.MetadataOnly, true, false, false)]
    [InlineData(RootAuthorizationScope.MetadataAndContent, true, true, false)]
    [InlineData(RootAuthorizationScope.ControlledDemo, false, false, true)]
    [InlineData(RootAuthorizationScope.Organize, false, false, true)]
    public void EachScopeGrantsExactlyWhatItSays(
        RootAuthorizationScope scope,
        bool metadata,
        bool content,
        bool mutate)
    {
        var root = Root(scope);

        Assert.Equal(metadata, RootCapabilities.CanReadMetadata(root));
        Assert.Equal(content, RootCapabilities.CanReadContent(root));
        Assert.Equal(mutate, RootCapabilities.CanMutate(root));
    }

    /// <summary>
    /// Agreeing to DeskAI listing file names is not agreeing to it reading what is inside
    /// them. Reusing the first consent for the second would take a permission never given.
    /// </summary>
    [Fact]
    public void MetadataConsentIsNotContentConsent()
    {
        var root = Root(RootAuthorizationScope.MetadataOnly);

        Assert.True(RootCapabilities.CanReadMetadata(root));
        Assert.False(RootCapabilities.CanReadContent(root));
    }

    /// <summary>
    /// Being allowed to read a file says nothing about being allowed to move it. This is the
    /// escalation the gate exists to stop.
    /// </summary>
    [Fact]
    public void ContentAccessNeverBecomesPermissionToChangeFiles()
    {
        var root = Root(RootAuthorizationScope.MetadataAndContent);

        Assert.True(RootCapabilities.CanReadContent(root));
        Assert.False(RootCapabilities.CanMutate(root));
    }

    /// <summary>
    /// A folder connected to be organized was never connected to have its contents read.
    /// The two consents are separate in both directions.
    /// </summary>
    [Fact]
    public void PermissionToChangeFilesIsNotPermissionToReadThem()
    {
        Assert.False(RootCapabilities.CanReadContent(Root(RootAuthorizationScope.Organize)));
        Assert.False(RootCapabilities.CanReadContent(Root(RootAuthorizationScope.ControlledDemo)));
    }

    [Theory]
    [InlineData(RootAccessLevel.Restricted)]
    [InlineData(RootAccessLevel.Protected)]
    public void AFolderThatIsNotAllowedGrantsNothingWhateverItWasConnectedFor(RootAccessLevel permission)
    {
        foreach (var scope in Enum.GetValues<RootAuthorizationScope>())
        {
            var root = Root(scope, permission);

            Assert.False(RootCapabilities.CanReadMetadata(root));
            Assert.False(RootCapabilities.CanReadContent(root));
            Assert.False(RootCapabilities.CanMutate(root));
        }
    }

    /// <summary>
    /// Scopes are stored as numbers, so their values must never shift. Reordering the enum
    /// would silently re-label folders someone already connected: a metadata-only grant
    /// could come back as permission to change files.
    /// </summary>
    [Fact]
    public void StoredScopeNumbersNeverMove()
    {
        Assert.Equal(0, (int)RootAuthorizationScope.MetadataOnly);
        Assert.Equal(1, (int)RootAuthorizationScope.ControlledDemo);
        Assert.Equal(2, (int)RootAuthorizationScope.Organize);
        Assert.Equal(3, (int)RootAuthorizationScope.MetadataAndContent);
    }

    /// <summary>
    /// A scope added later must arrive with no rights until someone grants them here, on
    /// purpose. This fails the moment a value is added without a row in the matrix above,
    /// which is the reminder to decide what it may do rather than inherit an answer.
    /// </summary>
    [Fact]
    public void EveryScopeIsAccountedForInTheMatrix()
    {
        Assert.Equal(4, Enum.GetValues<RootAuthorizationScope>().Length);
    }

    /// <summary>
    /// Tidying is its own yes. A folder connected for reading cannot be changed until someone
    /// allows tidying it, and allowing it changes nothing about what may be read.
    /// </summary>
    [Theory]
    [InlineData(RootAuthorizationScope.MetadataOnly, false)]
    [InlineData(RootAuthorizationScope.MetadataAndContent, true)]
    public void AReadingFolderCanBeTidiedOnlyAfterTidyingIsAllowed(RootAuthorizationScope scope, bool content)
    {
        var root = Root(scope);
        Assert.False(RootCapabilities.CanTidy(root));
        Assert.False(RootCapabilities.CanMutate(root));

        var allowed = root.WithTidyAllowedSince(DateTimeOffset.UnixEpoch);

        Assert.True(RootCapabilities.CanTidy(allowed));
        Assert.True(RootCapabilities.CanMutate(allowed));
        Assert.True(RootCapabilities.CanReadMetadata(allowed));
        Assert.Equal(content, RootCapabilities.CanReadContent(allowed));
    }

    [Theory]
    [InlineData(RootAuthorizationScope.ControlledDemo)]
    [InlineData(RootAuthorizationScope.Organize)]
    public void ATidyPermissionMeansNothingOnAFolderThatWasNotConnectedForReading(RootAuthorizationScope scope)
    {
        Assert.False(RootCapabilities.CanTidy(Root(scope).WithTidyAllowedSince(DateTimeOffset.UnixEpoch)));
    }

    [Theory]
    [InlineData(RootAccessLevel.Restricted)]
    [InlineData(RootAccessLevel.Protected)]
    public void ATidyPermissionOnAFolderThatIsNotAllowedGrantsNothing(RootAccessLevel permission)
    {
        var root = Root(RootAuthorizationScope.MetadataOnly, permission).WithTidyAllowedSince(DateTimeOffset.UnixEpoch);
        Assert.False(RootCapabilities.CanTidy(root));
        Assert.False(RootCapabilities.CanMutate(root));
    }

    private static AuthorizedRoot Root(
        RootAuthorizationScope scope,
        RootAccessLevel permission = RootAccessLevel.Allowed) =>
        AuthorizedRoot.Create(
            Guid.NewGuid(),
            Path.Combine(Path.GetTempPath(), "deskai-tests", "capabilities"),
            "Capabilities",
            permission,
            scope);
}
