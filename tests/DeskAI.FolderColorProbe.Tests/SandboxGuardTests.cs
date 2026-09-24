using DeskAI.FolderColorProbe;

namespace DeskAI.FolderColorProbe.Tests;

public sealed class SandboxGuardTests
{
    [Fact]
    public void Allows_only_the_sandbox_account() =>
        Assert.Null(SandboxGuard.Check("WDAGUtilityAccount"));

    [Theory]
    [InlineData("Hammouri")]
    [InlineData("Administrator")]
    [InlineData("")]
    public void Refuses_every_user_except_the_sandbox_account(string user) =>
        Assert.Contains("only inside Windows Sandbox", SandboxGuard.Check(user), StringComparison.Ordinal);

    [Theory]
    [InlineData("wdagutilityaccount")]
    [InlineData("WDAGUtilityAccount2")]
    [InlineData(" WDAGUtilityAccount")]
    public void Refuses_a_look_alike_name(string user) =>
        Assert.NotNull(SandboxGuard.Check(user));
}
