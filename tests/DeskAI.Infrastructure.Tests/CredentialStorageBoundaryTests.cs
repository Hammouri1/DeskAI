using DeskAI.Core.Abstractions;
using DeskAI.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace DeskAI.Infrastructure.Tests;

public sealed class CredentialStorageBoundaryTests
{
    [Fact]
    public void DependencyInjection_UsesWindowsCredentialManagerOnWindows()
    {
        var services = new ServiceCollection();
        services.AddDeskAiInfrastructure(options => options.DatabasePath = Path.Combine(Path.GetTempPath(), "unused-deskai-test.db"));
        using var provider = services.BuildServiceProvider();

        var vault = provider.GetRequiredService<ICredentialVault>();

        Assert.Equal(
            OperatingSystem.IsWindows() ? "WindowsCredentialVault" : "UnsupportedCredentialVault",
            vault.GetType().Name);
    }
}
