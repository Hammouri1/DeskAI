using DeskAI.AI.Transport;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;

namespace DeskAI.AI;

public sealed class ConfiguredSuggestionProvider(
    IAiSettingsRepository settingsRepository,
    ICredentialVault credentialVault,
    IAiHttpTransport transport) : IOrganizationSuggestionProvider
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

        return settings.Mode switch
        {
            AiMode.RuleEngineOnly => await new NoAiSuggestionProvider()
                .SuggestAsync(request, cancellationToken).ConfigureAwait(false),
            AiMode.Local when settings.Endpoint is not null => await new LocalOpenAiCompatibleSuggestionProvider(
                    transport, settings.Endpoint, settings.ModelId)
                .SuggestAsync(request, cancellationToken).ConfigureAwait(false),
            AiMode.Cloud when !settings.CloudConsentGranted => Refused(
                "Cloud AI is selected but disclosure consent has not been granted."),
            AiMode.Cloud when settings.ProviderId == "gemini" && settings.CredentialReference is not null =>
                await new GeminiSuggestionProvider(
                        transport, credentialVault, settings.CredentialReference, settings.ModelId)
                    .SuggestAsync(request, cancellationToken).ConfigureAwait(false),
            _ => Refused("The selected AI provider is not configured. DeskAI did not use another provider."),
        };
    }

    private static OrganizationSuggestionResponse Refused(string message) =>
        new(AiProviderStatus.Disabled, "AI unavailable", [], message);
}
