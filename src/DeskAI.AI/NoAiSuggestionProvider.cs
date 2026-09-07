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
            IsAvailable: false,
            Suggestions: [],
            UnavailableReason: "DeskAI is running in Rule Engine Only mode."));
    }
}
