namespace DeskAI.Core.Ai;

/// <summary>
/// Asks whichever AI is set up to answer one throwaway word, so a person can see for
/// themselves whether their choice actually works.
/// </summary>
/// <remarks>
/// The check carries nothing about the computer: no file, folder, name, size, or path is in
/// the request, whatever the sharing choices allow, because "does this work" needs no data to
/// answer. The reply is read only far enough to know that something answered; nothing in it
/// is believed, shown, or acted on. Like every other AI path, this holds no filesystem,
/// executor, or credential-enumeration capability — only the one saved key for the one
/// service the person picked.
/// </remarks>
public interface IAiConnectionCheck
{
    Task<AiConnectionResult> CheckAsync(CancellationToken cancellationToken = default);
}

/// <param name="ProviderDisplayName">The service that was asked, or "AI" when nothing was.</param>
/// <param name="Message">One plain sentence for the person, saying what to do next when it failed.</param>
public sealed record AiConnectionResult(
    AiProviderStatus Status,
    string ProviderDisplayName,
    string Message)
{
    public bool Worked => Status == AiProviderStatus.Success;
}
