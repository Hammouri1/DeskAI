using DeskAI.Core.Plans;
using DeskAI.Core.Roots;
using DeskAI.Core.Templates;
using DeskAI.Safety;

namespace DeskAI.Safety.Tests;

/// <summary>
/// Every folder a template could make, built in or typed, is checked by the real path policy
/// against a generated folder. A name the policy would refuse must not be in the catalog, and a
/// name the typed-name check lets through must still be one the policy accepts, because the
/// policy is the second gate and the executor runs it again.
/// </summary>
public sealed class FolderTemplatePolicyTests
{
    [Fact]
    public void Every_catalog_folder_name_passes_the_path_policy_inside_a_connected_folder()
    {
        var policy = new WindowsPathPolicy();
        var root = Root();

        foreach (var template in FolderTemplateCatalog.All)
        {
            foreach (var name in template.Folders)
            {
                var result = policy.ValidateRelativePath(root, name);
                Assert.True(result.Status == ValidationStatus.Allowed, $"{template.Name}: {name} — {result.Explanation}");
            }
        }
    }

    [Theory]
    [InlineData("Tax 2026")]
    [InlineData("v1.0")]
    [InlineData(".hidden")]
    [InlineData("Notes (old)")]
    public void A_typed_name_the_name_check_accepts_also_passes_the_policy(string name)
    {
        Assert.Null(FolderNameCheck.Check(name));
        Assert.Equal(ValidationStatus.Allowed, new WindowsPathPolicy().ValidateRelativePath(Root(), name).Status);
    }

    [Theory]
    [InlineData(@"..\Outside")]
    [InlineData(@"C:\Windows")]
    [InlineData("CON")]
    [InlineData("Notes.")]
    [InlineData("a?b")]
    public void A_name_the_name_check_refuses_is_also_refused_by_the_policy(string name)
    {
        Assert.NotNull(FolderNameCheck.Check(name));
        Assert.Equal(ValidationStatus.Blocked, new WindowsPathPolicy().ValidateRelativePath(Root(), name).Status);
    }

    private static AuthorizedRoot Root() =>
        AuthorizedRoot.Create(
            Guid.NewGuid(),
            Path.Combine(Path.GetTempPath(), "deskai-tests", "Downloads"),
            "Downloads",
            RootAccessLevel.Allowed,
            RootAuthorizationScope.MetadataOnly).WithTidyAllowedSince(DateTimeOffset.UnixEpoch);
}
