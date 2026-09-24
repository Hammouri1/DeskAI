using DeskAI.Core.Ai;

namespace DeskAI.AI;

public sealed class NoAiSuggestionProvider : IOrganizationSuggestionProvider
{
    public Task<OrganizationSuggestionResponse> SuggestAsync(
        OrganizationSuggestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new OrganizationSuggestionResponse(
            AiProviderStatus.Disabled,
            "AI is off",
            [],
            "AI is off. DeskAI continues using deterministic rules."));
    }

    public Task<AiSentenceResponse> ReadSentenceAsync(
        AiSentenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AiSentenceResponse(
            AiProviderStatus.Disabled,
            "AI is off",
            null,
            "AI is off. Turn it on in Privacy and AI first."));
    }

    public Task<AiGroupingResponse> GroupItemsAsync(
        AiGroupingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AiGroupingResponse(
            AiProviderStatus.Disabled,
            "AI is off",
            null,
            "AI is off. Turn it on in Privacy and AI first."));
    }
}
