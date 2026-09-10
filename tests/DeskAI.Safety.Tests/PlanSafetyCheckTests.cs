using DeskAI.Core.Roots;

namespace DeskAI.Safety.Tests;

/// <summary>
/// The safety policy as Core sees it. The protected check is what keeps a protected file out
/// of anything sent to AI, independently of the scanner having skipped it already.
/// </summary>
public sealed class PlanSafetyCheckTests
{
    private static readonly AuthorizedRoot Folder = AuthorizedRoot.Create(
        Guid.NewGuid(), @"C:\DeskAITests\Tidy", "Tidy", RootAccessLevel.Allowed, RootAuthorizationScope.MetadataOnly);

    [Fact]
    public void A_protected_file_inside_a_connected_folder_is_reported_as_protected()
    {
        var policy = new WindowsPathPolicy(userProtectedEntries: [@"C:\DeskAITests\Tidy\tax-return.pdf"]);
        var check = new PlanSafetyCheck(new PlanValidator(policy), policy);

        Assert.True(check.IsProtected(Folder, "tax-return.pdf"));
        Assert.False(check.IsProtected(Folder, "notes.pdf"));
    }

    [Theory]
    [InlineData(@"..\outside.pdf")]
    [InlineData(@"C:\Windows\win.ini")]
    [InlineData("notes.pdf:hidden")]
    public void A_path_the_policy_refuses_is_treated_as_protected(string relativePath)
    {
        var policy = new WindowsPathPolicy();
        var check = new PlanSafetyCheck(new PlanValidator(policy), policy);

        Assert.True(check.IsProtected(Folder, relativePath));
    }

    [Fact]
    public void Nothing_in_a_protected_folder_may_be_described()
    {
        var policy = new WindowsPathPolicy();
        var check = new PlanSafetyCheck(new PlanValidator(policy), policy);
        var protectedFolder = AuthorizedRoot.Create(
            Guid.NewGuid(), @"C:\DeskAITests\Tidy", "Tidy", RootAccessLevel.Protected, RootAuthorizationScope.MetadataOnly);

        Assert.True(check.IsProtected(protectedFolder, "notes.pdf"));
    }
}
