using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;
using DeskAI.Core.Appearance;
using DeskAI.Core.Desktop;
using DeskAI.Core.Rules;
using DeskAI.Core.Search;

namespace DeskAI.Core.Backup;

/// <summary>What Start fresh forgot. Every number is DeskAI's own memory; no file was touched.</summary>
public sealed record FreshStartOutcome(int FoldersForgotten, int RulesRemoved, int SearchesRemoved, int KeysRemoved);

/// <summary>
/// Forgets everything DeskAI remembers, so it is as it was on first run.
/// </summary>
/// <remarks>
/// <para>
/// Folders are disconnected through the same service the Search page uses, which erases each
/// folder's index, permissions, plans, and tidy history in one transaction. Every rule and saved
/// search is removed, the AI choice goes back to off and every catalog service's key is removed
/// from Windows, the check history and settings and the look are reset, the remembered
/// wallpaper is forgotten, and the first-run welcome will greet the person again.
/// </para>
/// <para>
/// It holds repositories, the folder service, and the credential vault, and nothing that can
/// reach a file on disk: no scanner, reader, executor, or journal. Files a tidy moved stay
/// exactly where they are, and the wallpaper Windows is showing is left as it is.
/// </para>
/// </remarks>
public sealed class FreshStartService(
    ConnectedFolderService folders,
    IAuthorizedRootRepository roots,
    IRuleRepository rules,
    ISavedSearchRepository searches,
    IAiSettingsRepository aiSettings,
    ICredentialVault vault,
    IAutomaticCheckHistoryRepository checkHistory,
    IAutomaticCheckSettingsRepository checkSettings,
    IAppearanceSettingsRepository appearance,
    IAppSettingsStore appSettings)
{
    private readonly ConnectedFolderService _folders = folders;
    private readonly IAuthorizedRootRepository _roots = roots;
    private readonly IRuleRepository _rules = rules;
    private readonly ISavedSearchRepository _searches = searches;
    private readonly IAiSettingsRepository _aiSettings = aiSettings;
    private readonly ICredentialVault _vault = vault;
    private readonly IAutomaticCheckHistoryRepository _checkHistory = checkHistory;
    private readonly IAutomaticCheckSettingsRepository _checkSettings = checkSettings;
    private readonly IAppearanceSettingsRepository _appearance = appearance;
    private readonly IAppSettingsStore _appSettings = appSettings;

    public async Task<FreshStartOutcome> StartFreshAsync(CancellationToken cancellationToken = default)
    {
        var foldersForgotten = 0;
        foreach (var folder in await _folders.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            await _folders.DisconnectAsync(folder.Id, cancellationToken).ConfigureAwait(false);
            foldersForgotten++;
        }

        // Anything the folder list does not show — an old practice workspace from an earlier
        // version, for instance — is still DeskAI's memory and goes too.
        foreach (var root in await _roots.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            await _roots.RemoveAsync(root.Id, cancellationToken).ConfigureAwait(false);
            foldersForgotten++;
        }

        var rulesRemoved = 0;
        foreach (var rule in await _rules.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            await _rules.RemoveAsync(rule.Id, cancellationToken).ConfigureAwait(false);
            rulesRemoved++;
        }

        var searchesRemoved = 0;
        foreach (var search in await _searches.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            await _searches.RemoveAsync(search.Id, cancellationToken).ConfigureAwait(false);
            searchesRemoved++;
        }

        // Every service's key, not only the chosen one: a key saved for a service that was
        // later switched away from is still a key DeskAI holds.
        var keysRemoved = 0;
        foreach (var provider in CloudProviderCatalog.All)
        {
            if (await _vault.RetrieveAsync(provider.CredentialReference, cancellationToken).ConfigureAwait(false) is not null)
            {
                keysRemoved++;
            }

            await _vault.RemoveAsync(provider.CredentialReference, cancellationToken).ConfigureAwait(false);
        }

        await _aiSettings.SaveAsync(AiSettings.Default, cancellationToken).ConfigureAwait(false);
        await _checkHistory.ClearAsync(cancellationToken).ConfigureAwait(false);
        await _checkSettings.SaveAsync(AutomaticCheckSettings.Default, cancellationToken).ConfigureAwait(false);
        await _appearance.SaveAsync(AppearanceSettings.Default, cancellationToken).ConfigureAwait(false);
        await _appSettings.RemoveAsync(WallpaperService.PreviousKey, cancellationToken).ConfigureAwait(false);
        await _appSettings.RemoveAsync(WallpaperService.SetKey, cancellationToken).ConfigureAwait(false);
        await _appSettings.RemoveAsync(Ai.AskDeskAiService.AgreedKey, cancellationToken).ConfigureAwait(false);
        await _appSettings.RemoveAsync(Welcome.WelcomeService.ShownKey, cancellationToken).ConfigureAwait(false);
        foreach (var key in QuickSearch.QuickSearchSettingsService.Keys)
        {
            await _appSettings.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }

        return new FreshStartOutcome(foldersForgotten, rulesRemoved, searchesRemoved, keysRemoved);
    }
}
