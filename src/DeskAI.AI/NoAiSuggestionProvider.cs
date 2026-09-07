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
            "Rule Engine Only",
            [],
            "AI is off. DeskAI continues using deterministic rules."));
    }
}
