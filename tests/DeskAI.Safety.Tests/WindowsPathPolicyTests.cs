using DeskAI.Core.Plans;
using DeskAI.Core.Roots;

namespace DeskAI.Safety.Tests;

public sealed class WindowsPathPolicyTests
{
    private static readonly AuthorizedRoot Root = AuthorizedRoot.Create(
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        @"C:\DeskAITests\AuthorizedRoot",
        "Synthetic root",
        RootAccessLevel.Allowed, RootAuthorizationScope.Organize);

    [Theory]
    [InlineData(@"Documents\report.pdf")]
    [InlineData("Images/screenshot.png")]
    [InlineData(@"folder\nested\file.txt")]
    public void ValidateRelativePath_AllowsNormalContainedPath(string relativePath)
    {
        var result = new WindowsPathPolicy().ValidateRelativePath(Root, relativePath);

        Assert.Equal(ValidationStatus.Allowed, result.Status);
    }

    [Theory]
    [InlineData(@"..\escape.txt")]
    [InlineData(@"folder\..\..\escape.txt")]
    public void ValidateRelativePath_BlocksTraversal(string relativePath)
    {
        var result = new WindowsPathPolicy().ValidateRelativePath(Root, relativePath);

        Assert.Equal(ValidationStatus.Blocked, result.Status);
        Assert.Equal(ValidationReasonCode.PathTraversal, result.ReasonCode);
    }

    [Theory]
    [InlineData(@"C:\Windows\system.ini")]
    [InlineData(@"\\server\share\file.txt")]
    [InlineData(@"file.txt:secret")]
    [InlineData(@"folder\NUL.txt")]
    [InlineData(@"folder\trailing. ")]
    public void ValidateRelativePath_BlocksUnsupportedOrAbsoluteForms(string path)
    {
        var result = new WindowsPathPolicy().ValidateRelativePath(Root, path);

        Assert.Equal(ValidationStatus.Blocked, result.Status);
    }

    [Fact]
    public void ValidateRelativePath_DoesNotAcceptPrefixConfusion()
    {
        var result = new WindowsPathPolicy().ValidateRelativePath(Root, @"..\AuthorizedRootOther\file.txt");

        Assert.Equal(ValidationReasonCode.PathTraversal, result.ReasonCode);
    }

    [Fact]
    public void ValidateRelativePath_BlocksUserProtectedEntry()
    {
        var policy = new WindowsPathPolicy(
            userProtectedEntries: [@"C:\DeskAITests\AuthorizedRoot\Private"]);

        var result = policy.ValidateRelativePath(Root, @"Private\secret.txt");

        Assert.Equal(ValidationReasonCode.ProtectedEntry, result.ReasonCode);
    }

    [Fact]
    public void ValidateRelativePath_BlocksPermanentlyProtectedLocation()
    {
        var policy = new WindowsPathPolicy(
            permanentlyProtectedRoots: [@"C:\DeskAITests\AuthorizedRoot\ApplicationState"]);

        var result = policy.ValidateRelativePath(Root, @"ApplicationState\deskai.db");

        Assert.Equal(ValidationReasonCode.ProtectedRoot, result.ReasonCode);
    }

    [Fact]
    public void ValidateRelativePath_BlocksProtectedRootPermission()
    {
        var protectedRoot = AuthorizedRoot.Create(
            Guid.NewGuid(),
            @"C:\DeskAITests\AuthorizedRoot",
            "Protected synthetic root",
            RootAccessLevel.Protected, RootAuthorizationScope.Organize);

        var result = new WindowsPathPolicy().ValidateRelativePath(protectedRoot, "file.txt");

        Assert.Equal(ValidationReasonCode.ProtectedRoot, result.ReasonCode);
    }

    [Fact]
    public void ValidateRoot_BlocksARootInsideAProtectedLocation()
    {
        var policy = new WindowsPathPolicy(permanentlyProtectedRoots: [@"C:\DeskAITests"]);

        var result = policy.ValidateRoot(Root);

        Assert.Equal(ValidationStatus.Blocked, result.Status);
        Assert.Equal(ValidationReasonCode.ProtectedRoot, result.ReasonCode);
    }

    /// <summary>
    /// Found 2026-09-16: DeskAI's own program folder is protected, and when DeskAI runs from a
    /// folder on the Desktop the Desktop "overlapped" it and could not be connected. A folder
    /// that merely contains a protected place may be connected; the protected part is skipped
    /// entry by entry, which the second half of this test pins down.
    /// </summary>
    [Fact]
    public void ValidateRoot_WarnsButAllowsARootThatContainsAProtectedLocation()
    {
        var policy = new WindowsPathPolicy(
            permanentlyProtectedRoots: [@"C:\DeskAITests\AuthorizedRoot\DeskAI\app"]);

        var result = policy.ValidateRoot(Root);

        Assert.Equal(ValidationStatus.Warning, result.Status);
        Assert.Equal(ValidationReasonCode.ProtectedRoot, result.ReasonCode);
        Assert.Equal(ValidationStatus.Blocked, policy.ValidateRelativePath(Root, @"DeskAI\app\DeskAI.App.exe").Status);
        Assert.Equal(ValidationStatus.Blocked, policy.ValidateRelativePath(Root, @"DeskAI\app").Status);
        Assert.Equal(ValidationStatus.Blocked, policy.ValidateRelativePath(Root, @"DeskAI").Status);
        Assert.Equal(ValidationStatus.Allowed, policy.ValidateRelativePath(Root, @"DeskAI-notes.txt").Status);
    }
}
