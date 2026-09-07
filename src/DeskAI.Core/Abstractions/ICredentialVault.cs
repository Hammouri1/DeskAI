namespace DeskAI.Core.Abstractions;

public interface ICredentialVault
{
    Task SaveAsync(string reference, string secret, CancellationToken cancellationToken = default);

    Task<string?> RetrieveAsync(string reference, CancellationToken cancellationToken = default);

    Task RemoveAsync(string reference, CancellationToken cancellationToken = default);
}
