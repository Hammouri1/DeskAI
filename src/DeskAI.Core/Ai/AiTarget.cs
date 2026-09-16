namespace DeskAI.Core.Ai;

/// <summary>Who would be asked, and where that is. Not set up means nothing can be sent.</summary>
public sealed record AiTarget(bool IsSetUp, string Name, string Destination)
{
    /// <summary>
    /// Reads the saved AI choice into a target. Local AI counts only at a loopback address;
    /// online AI only with consent, a saved key reference, and a service from the closed catalog.
    /// </summary>
    public static AiTarget Of(AiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Mode == AiMode.Local &&
            Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var local) &&
            local.IsLoopback)
        {
            return new(true, "Local AI", $"{local.Authority} on this computer");
        }

        if (settings.Mode == AiMode.Cloud &&
            settings.CloudConsentGranted &&
            settings.CredentialReference is not null &&
            CloudProviderCatalog.Find(settings.ProviderId) is { } provider)
        {
            return new(true, provider.DisplayName, provider.ChatCompletionsEndpoint.Host);
        }

        return new(false, "AI", string.Empty);
    }
}
