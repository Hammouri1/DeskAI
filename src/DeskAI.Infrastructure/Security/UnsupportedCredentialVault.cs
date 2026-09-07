using DeskAI.Core.Abstractions;

namespace DeskAI.Infrastructure.Security;

internal sealed class UnsupportedCredentialVault : ICredentialVault
{
    public Task SaveAsync(string reference, string secret, CancellationToken cancellationToken = default) =>
        Task.FromException(new PlatformNotSupportedException("DeskAI credentials require Windows Credential Manager."));

    public Task<string?> RetrieveAsync(string reference, CancellationToken cancellationToken = default) =>
        Task.FromException<string?>(new PlatformNotSupportedException("DeskAI credentials require Windows Credential Manager."));

    public Task RemoveAsync(string reference, CancellationToken cancellationToken = default) =>
        Task.FromException(new PlatformNotSupportedException("DeskAI credentials require Windows Credential Manager."));
}
