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

    /// <inheritdoc />
    /// <remarks>
    /// The same consent, catalog, and daily-cap rules as asking about files. There is no sharing
    /// check because a sentence carries nothing about a file; the person sees the sentence itself
    /// in the dialog before Send.
    /// </remarks>
    public async Task<AiSentenceResponse> ReadSentenceAsync(
        AiSentenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = await settingsRepository.LoadAsync(cancellationToken).ConfigureAwait(false);
        var cloudProvider = CloudProviderCatalog.Find(settings.ProviderId);
        if (!await ReserveOnlineRequestAsync(settings, cloudProvider, cancellationToken).ConfigureAwait(false))
        {
            return new AiSentenceResponse(
                AiProviderStatus.CostLimitReached,
                "AI unavailable",
                null,
                "You have reached today's online AI limit. Nothing was sent.");
        }

        return settings.Mode switch
        {
            AiMode.RuleEngineOnly => await new NoAiSuggestionProvider()
                .ReadSentenceAsync(request, cancellationToken).ConfigureAwait(false),
            AiMode.Local when settings.Endpoint is not null => await new LocalOpenAiCompatibleSuggestionProvider(
                    transport, settings.Endpoint, settings.ModelId)
                .ReadSentenceAsync(request, cancellationToken).ConfigureAwait(false),
            AiMode.Cloud when !settings.CloudConsentGranted => RefusedSentence(
                "Online AI is selected, but sharing has not been approved."),
            AiMode.Cloud when cloudProvider is not null && settings.CredentialReference is not null =>
                await new CloudChatCompletionsSuggestionProvider(
                        transport, credentialVault, cloudProvider, settings.ModelId)
                    .ReadSentenceAsync(request, cancellationToken).ConfigureAwait(false),
            _ => RefusedSentence("AI is not set up yet. DeskAI did not send anything."),
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// The same consent, catalog, and daily-cap rules as reading a sentence, plus the sharing
    /// check: online AI is used only when the saved choices allow file types, file names, and
    /// folder names (ADR 0042). The grouping service checks this too; this is the last gate.
    /// </remarks>
    public async Task<AiGroupingResponse> GroupItemsAsync(
        AiGroupingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = await settingsRepository.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (settings.Mode == AiMode.Cloud && AiGroupingRequest.Discloses.Any(c => !settings.CloudDisclosures.Contains(c)))
        {
            return new AiGroupingResponse(
                AiProviderStatus.SafetyRejected,
                "AI unavailable",
                null,
                "Your sharing choices do not allow file types, file names, and folder names, so nothing was sent.");
        }

        var cloudProvider = CloudProviderCatalog.Find(settings.ProviderId);
        if (!await ReserveOnlineRequestAsync(settings, cloudProvider, cancellationToken).ConfigureAwait(false))
        {
            return new AiGroupingResponse(
                AiProviderStatus.CostLimitReached,
                "AI unavailable",
                null,
                "You have reached today's online AI limit. Nothing was sent.");
        }

        return settings.Mode switch
        {
            AiMode.RuleEngineOnly => await new NoAiSuggestionProvider()
                .GroupItemsAsync(request, cancellationToken).ConfigureAwait(false),
            AiMode.Local when settings.Endpoint is not null => await new LocalOpenAiCompatibleSuggestionProvider(
                    transport, settings.Endpoint, settings.ModelId)
                .GroupItemsAsync(request, cancellationToken).ConfigureAwait(false),
            AiMode.Cloud when !settings.CloudConsentGranted => AiGroupingResponse.From(RefusedSentence(
                "Online AI is selected, but sharing has not been approved.")),
            AiMode.Cloud when cloudProvider is not null && settings.CredentialReference is not null =>
                await new CloudChatCompletionsSuggestionProvider(
                        transport, credentialVault, cloudProvider, settings.ModelId)
                    .GroupItemsAsync(request, cancellationToken).ConfigureAwait(false),
            _ => AiGroupingResponse.From(RefusedSentence("AI is not set up yet. DeskAI did not send anything.")),
        };
    }

    /// <summary>
    /// Counts one online request against today's cap, per provider. True when nothing needs
    /// counting (not online, or not set up) or the cap still has room.
    /// </summary>
    private async Task<bool> ReserveOnlineRequestAsync(AiSettings settings, CloudProvider? cloudProvider, CancellationToken cancellationToken) =>
        !(settings.Mode == AiMode.Cloud && settings.CloudConsentGranted &&
          cloudProvider is not null && settings.CredentialReference is not null) ||
        await usageBudget.TryReserveRequestAsync(
            cloudProvider.Id,
            Math.Clamp(settings.DailyRequestLimit, 1, 1000),
            DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
            cancellationToken).ConfigureAwait(false);

    private static OrganizationSuggestionResponse Refused(string message) =>
        new(AiProviderStatus.Disabled, "AI unavailable", [], message);

    private static AiSentenceResponse RefusedSentence(string message) =>
        new(AiProviderStatus.Disabled, "AI unavailable", null, message);
}
