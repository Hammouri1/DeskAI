using DeskAI.AI.Transport;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;

namespace DeskAI.AI;

public sealed class ConfiguredSuggestionProvider(
    IAiSettingsRepository settingsRepository,
    ICredentialVault credentialVault,
    IAiHttpTransport transport,
    IAiUsageBudget usageBudget,
    IClock clock) : IOrganizationSuggestionProvider
{
    public async Task<OrganizationSuggestionResponse> SuggestAsync(
        OrganizationSuggestionRequest request,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsRepository.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (settings.Mode == AiMode.Cloud &&
            request.Disclosure.Categories.Any(category => !settings.CloudDisclosures.Contains(category)))
        {
            return new OrganizationSuggestionResponse(
                AiProviderStatus.SafetyRejected,
                "AI unavailable",
                [],
                "The request asks for data outside your saved cloud-sharing choices.");
        }

        // The daily cap is counted per provider, so choosing a different service does not
        // hand the user a fresh allowance for the one they already used today.
        var cloudProvider = CloudProviderCatalog.Find(settings.ProviderId);
        if (settings.Mode == AiMode.Cloud && settings.CloudConsentGranted &&
            cloudProvider is not null && settings.CredentialReference is not null &&
            !await usageBudget.TryReserveRequestAsync(
                cloudProvider.Id,
                Math.Clamp(settings.DailyRequestLimit, 1, 1000),
                DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
                cancellationToken).ConfigureAwait(false))
        {
            return new OrganizationSuggestionResponse(
                AiProviderStatus.CostLimitReached,
                "AI unavailable",
                [],
                "You have reached today's online AI limit. Nothing was sent.");
        }

        return settings.Mode switch
        {
            AiMode.RuleEngineOnly => await new NoAiSuggestionProvider()
                .SuggestAsync(request, cancellationToken).ConfigureAwait(false),
            AiMode.Local when settings.Endpoint is not null => await new LocalOpenAiCompatibleSuggestionProvider(
                    transport, settings.Endpoint, settings.ModelId)
                .SuggestAsync(request, cancellationToken).ConfigureAwait(false),
            AiMode.Cloud when !settings.CloudConsentGranted => Refused(
                "Online AI is selected, but sharing has not been approved."),
            // The saved provider must still be one DeskAI knows. An unrecognized ID is
            // refused rather than guessed at, so a tampered setting cannot pick a
            // destination or reuse another provider's saved key.
            AiMode.Cloud when cloudProvider is not null && settings.CredentialReference is not null =>
                await new CloudChatCompletionsSuggestionProvider(
                        transport, credentialVault, cloudProvider, settings.ModelId)
                    .SuggestAsync(request, cancellationToken).ConfigureAwait(false),
            _ => Refused("AI is not set up yet. DeskAI did not send anything."),
        };
    }

    private static OrganizationSuggestionResponse Refused(string message) =>
        new(AiProviderStatus.Disabled, "AI unavailable", [], message);
}
