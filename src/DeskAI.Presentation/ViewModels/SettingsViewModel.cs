using System.Globalization;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using DeskAI.AI;
using DeskAI.App.Services;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;
using DeskAI.Core.Backup;
using DeskAI.Core.Rules;

namespace DeskAI.App.ViewModels;

public sealed class SettingsViewModel(
    IAiSettingsRepository settingsRepository,
    IAuthorizedRootRepository rootRepository,
    ICredentialVault credentialVault,
    BackupService backup,
    FreshStartService freshStart,
    IUserFileStore files,
    BackgroundPresenceController presence,
    IClock clock) : ObservableObject
{
    private AiSettings _loaded = AiSettings.Default;
    private string _backupStatus = string.Empty;
    private string _freshStartStatus = string.Empty;

    /// <summary>The version people see, from the build. A release tag sets it.</summary>
    public static string Version { get; } =
        typeof(SettingsViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            is { Length: > 0 } informational
            ? "DeskAI " + informational.Split('+')[0]
            : "DeskAI";

    /// <summary>The name the save dialog suggests for a backup file, dated so two are told apart.</summary>
    public string SuggestedBackupFileName =>
        $"DeskAI backup {clock.UtcNow.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.json";

    /// <summary>What the last backup or restore did, written under those buttons.</summary>
    public string BackupStatus
    {
        get => _backupStatus;
        private set
        {
            if (SetProperty(ref _backupStatus, value))
            {
                OnPropertyChanged(nameof(HasBackupStatus));
            }
        }
    }

    public bool HasBackupStatus => !string.IsNullOrEmpty(BackupStatus);

    public string FreshStartStatus
    {
        get => _freshStartStatus;
        private set
        {
            if (SetProperty(ref _freshStartStatus, value))
            {
                OnPropertyChanged(nameof(HasFreshStartStatus));
            }
        }
    }

    public bool HasFreshStartStatus => !string.IsNullOrEmpty(FreshStartStatus);

    /// <summary>Writes every rule and saved search to the file the person chose. Nothing else goes in it.</summary>
    public async Task ExportBackupAsync(string path)
    {
        try
        {
            var made = await backup.ExportAsync();
            await files.WriteTextAsync(path, BackupService.ToText(made));
            BackupStatus = $"Saved {Count(made.Rules.Count, "rule")} and {Count(made.SavedSearches.Count, "saved search", "saved searches")} "
                + $"to {Path.GetFileName(path)}. The file holds no folders, keys, or locations.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            BackupStatus = $"DeskAI could not save the backup: {exception.Message}";
        }
    }

    /// <summary>
    /// What restoring the chosen file would add and skip. Adds nothing; the page shows it and
    /// only Restore in that dialog restores.
    /// </summary>
    /// <returns>The preview, or null when the reason is already written in <see cref="BackupStatus"/>.</returns>
    public async Task<RestorePreview?> PreviewRestoreAsync(string path)
    {
        try
        {
            var text = await files.ReadTextAsync(path, DeskAiBackup.MaxBytes);
            var preview = await backup.PreviewAsync(text);
            if (preview.Problem is not null)
            {
                BackupStatus = preview.Problem;
                return null;
            }

            BackupStatus = string.Empty;
            return preview;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            BackupStatus = $"DeskAI could not read that file: {exception.Message}";
            return null;
        }
    }

    /// <summary>Adds what the file holds. Restored rules arrive switched off.</summary>
    public async Task RestoreBackupAsync(string path)
    {
        try
        {
            var text = await files.ReadTextAsync(path, DeskAiBackup.MaxBytes);
            var outcome = await backup.RestoreAsync(text);
            if (outcome.Problem is not null)
            {
                BackupStatus = outcome.Problem;
                return;
            }

            var skipped = outcome.SkippedNames.Count == 0
                ? string.Empty
                : $" Skipped {outcome.SkippedNames.Count} you already had or DeskAI can't use: {string.Join(", ", outcome.SkippedNames)}.";
            var rulesNote = outcome.RulesAdded == 0
                ? string.Empty
                : " Restored rules are switched off — turn them on in Automatic tasks.";
            BackupStatus = $"Restored {Count(outcome.RulesAdded, "rule")} and {Count(outcome.SearchesAdded, "saved search", "saved searches")}.{skipped}{rulesNote}";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            BackupStatus = $"DeskAI could not restore that file: {exception.Message}";
        }
    }

    /// <summary>
    /// Forgets everything DeskAI remembers. Called only after the page's dialog was confirmed.
    /// </summary>
    /// <remarks>
    /// The icon near the clock is refreshed from the now-default settings, so a DeskAI that was
    /// keeping running with no window is no longer doing so. No file on disk is touched.
    /// </remarks>
    public async Task StartFreshAsync()
    {
        try
        {
            var outcome = await freshStart.StartFreshAsync();
            presence.Refresh(AutomaticCheckSettings.Default);
            await InitializeAsync();
            FreshStartStatus =
                $"Done. DeskAI forgot {Count(outcome.FoldersForgotten, "folder")}, {Count(outcome.RulesRemoved, "rule")}, "
                + $"{Count(outcome.SearchesRemoved, "saved search", "saved searches")}, and {Count(outcome.KeysRemoved, "saved key")}. "
                + "Your files were not touched.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            FreshStartStatus = $"DeskAI stopped part-way: {exception.Message}";
        }
    }

    private static string Count(int count, string singular, string? plural = null) =>
        count == 1 ? $"1 {singular}" : $"{count} {plural ?? singular + "s"}";

    private static bool IsExpectedFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or System.Data.Common.DbException
            or System.ComponentModel.Win32Exception;
    private bool _shareExtension = true;
    private bool _shareMetadata;
    private bool _shareFileName;
    private bool _shareFolderNames;
    private bool _shareFullPath;
    private string _authorizedFolderCount = "0";
    private string _saveStatus = "AI is off. Nothing is being shared.";
    private int _selectedModeIndex;
    private string _localEndpoint = string.Empty;
    private string _localModel = string.Empty;
    private string _cloudModel = string.Empty;
    private int _selectedCloudProviderIndex;
    private bool _cloudConsent;
    private string _providerStatus = "Choose whether you want to use AI.";
    private double _timeoutSeconds = 30;
    private double _dailyRequestLimit = 20;
    private string _keyWarning = string.Empty;

    public bool ShareExtension { get => _shareExtension; set => SetProperty(ref _shareExtension, value); }
    public bool ShareMetadata { get => _shareMetadata; set => SetProperty(ref _shareMetadata, value); }
    public bool ShareFileName { get => _shareFileName; set => SetProperty(ref _shareFileName, value); }
    public bool ShareFolderNames { get => _shareFolderNames; set => SetProperty(ref _shareFolderNames, value); }
    public bool ShareFullPath { get => _shareFullPath; set => SetProperty(ref _shareFullPath, value); }
    public string AuthorizedFolderCount => _authorizedFolderCount;
    public string SaveStatus => _saveStatus;
    public string AiProcessing => _loaded.Mode switch
    {
        AiMode.Local => "On this computer",
        AiMode.Cloud => $"Online with {SavedProviderName}",
        _ => "AI is off",
    };
    public string InternetUse => _loaded.Mode == AiMode.Cloud ? $"On — {SavedProviderName}" : "Off";
    public string CloudDataShared => _loaded.Mode == AiMode.Cloud
        ? FormatCategories(_loaded.CloudDisclosures)
        : "None — AI is off";
    public int SelectedModeIndex { get => _selectedModeIndex; set => SetProperty(ref _selectedModeIndex, value); }
    public string LocalEndpoint { get => _localEndpoint; set => SetProperty(ref _localEndpoint, value); }
    public string LocalModel { get => _localModel; set => SetProperty(ref _localModel, value); }
    public string CloudModel { get => _cloudModel; set => SetProperty(ref _cloudModel, value); }

    /// <summary>The services a person can pick between. The list is fixed in code.</summary>
    public IReadOnlyList<string> CloudProviderNames { get; } =
        [.. CloudProviderCatalog.All.Select(provider => provider.DisplayName)];

    public int SelectedCloudProviderIndex
    {
        get => _selectedCloudProviderIndex;
        set
        {
            if (SetProperty(ref _selectedCloudProviderIndex, value))
            {
                OnPropertyChanged(nameof(SelectedProviderName));
                OnPropertyChanged(nameof(CloudModelHint));
                OnPropertyChanged(nameof(CloudKeyHeader));
                OnPropertyChanged(nameof(CloudKeySourceHint));
                OnPropertyChanged(nameof(CloudConsentHeader));
                OnPropertyChanged(nameof(CloudProviderNote));
                OnPropertyChanged(nameof(RemoveKeyLabel));
            }
        }
    }

    private CloudProvider SelectedProvider =>
        CloudProviderCatalog.All[Math.Clamp(_selectedCloudProviderIndex, 0, CloudProviderCatalog.All.Count - 1)];

    private string SavedProviderName =>
        CloudProviderCatalog.Find(_loaded.ProviderId)?.DisplayName ?? "an online service";

    public string SelectedProviderName => SelectedProvider.DisplayName;
    public string CloudModelHint => SelectedProvider.ModelHint;
    public string CloudKeyHeader => $"Your {SelectedProvider.DisplayName} key";
    public string CloudKeySourceHint => $"Get a key from {SelectedProvider.KeySource}. Windows stores it safely and DeskAI never shows it again.";
    public string CloudConsentHeader => $"I agree to send only my choices above to {SelectedProvider.DisplayName}";
    public string CloudProviderNote =>
        $"{SelectedProvider.DisplayName} controls prices and how its service handles data. DeskAI sends no file contents in this version.";
    public string RemoveKeyLabel => $"Remove saved {SelectedProvider.DisplayName} key";
    public bool CloudConsent { get => _cloudConsent; set => SetProperty(ref _cloudConsent, value); }
    public string ProviderStatus => _providerStatus;
    public double TimeoutSeconds { get => _timeoutSeconds; set => SetProperty(ref _timeoutSeconds, value); }
    public double DailyRequestLimit { get => _dailyRequestLimit; set => SetProperty(ref _dailyRequestLimit, value); }

    public async Task InitializeAsync()
    {
        _loaded = await settingsRepository.LoadAsync();
        Apply(_loaded.CloudDisclosures);
        _selectedModeIndex = (int)_loaded.Mode;
        _localEndpoint = _loaded.Mode == AiMode.Local ? _loaded.Endpoint ?? string.Empty : string.Empty;
        _localModel = _loaded.Mode == AiMode.Local ? _loaded.ModelId : string.Empty;
        var savedProvider = CloudProviderCatalog.Find(_loaded.ProviderId);
        _cloudModel = savedProvider is not null ? _loaded.ModelId : string.Empty;
        _selectedCloudProviderIndex = savedProvider is null
            ? 0
            : CloudProviderCatalog.All.ToList().FindIndex(provider => provider.Id == savedProvider.Id);
        _cloudConsent = _loaded.CloudConsentGranted;
        _timeoutSeconds = _loaded.TimeoutSeconds;
        _dailyRequestLimit = _loaded.DailyRequestLimit;
        _authorizedFolderCount = (await rootRepository.ListAsync()).Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
        NotifyAll();
    }

    public string CloudConsentSummary() =>
        $"{SelectedProvider.DisplayName} will receive only: {FormatCategories(Selected())}. " +
        $"It is sent to {SelectedProvider.ChatCompletionsEndpoint.Host} and nowhere else. " +
        "File contents and protected files are always left out.";

    public async Task SaveProviderAsync(string apiKey)
    {
        _keyWarning = string.Empty;
        try
        {
            var mode = Enum.IsDefined((AiMode)SelectedModeIndex)
                ? (AiMode)SelectedModeIndex
                : AiMode.RuleEngineOnly;
            var updated = mode switch
            {
                AiMode.RuleEngineOnly => _loaded with
                {
                    Mode = mode,
                    ProviderId = "none",
                    Endpoint = null,
                    ModelId = string.Empty,
                    CloudConsentGranted = false,
                    CloudDisclosures = Selected(),
                },
                AiMode.Local => _loaded with
                {
                    Mode = mode,
                    ProviderId = "local-compatible",
                    Endpoint = ProviderEndpointPolicy.RequireLoopback(LocalEndpoint).AbsoluteUri,
                    ModelId = ProviderEndpointPolicy.RequireModelId(LocalModel),
                    CloudConsentGranted = false,
                    CloudDisclosures = Selected(),
                },
                AiMode.Cloud when CloudConsent => await CreateCloudSettingsAsync(apiKey),
                _ => throw new InvalidOperationException("Turn on the sharing agreement before using online AI."),
            };

            updated = updated with
            {
                TimeoutSeconds = Math.Clamp((int)TimeoutSeconds, 5, 120),
                DailyRequestLimit = Math.Clamp((int)DailyRequestLimit, 1, 1000),
            };

            await settingsRepository.SaveAsync(updated);
            _loaded = updated;
            _providerStatus = mode switch
            {
                AiMode.RuleEngineOnly => "Saved. DeskAI will work without AI.",
                AiMode.Local => "Saved. AI will run only on this computer.",
                _ => $"Saved. {SelectedProvider.DisplayName} is ready with your sharing choices.{_keyWarning}",
            };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _providerStatus = $"Settings were not enabled: {exception.Message}";
        }

        NotifyAll();
    }

    /// <summary>Removes only the key for the currently selected service, never every saved key.</summary>
    public async Task RemoveCloudKeyAsync()
    {
        var provider = SelectedProvider;
        try
        {
            await credentialVault.RemoveAsync(provider.CredentialReference);
            _loaded = _loaded with
            {
                Mode = AiMode.RuleEngineOnly,
                ProviderId = "none",
                CredentialReference = null,
                CloudConsentGranted = false,
            };
            await settingsRepository.SaveAsync(_loaded);
            _selectedModeIndex = (int)AiMode.RuleEngineOnly;
            _cloudConsent = false;
            _providerStatus = $"{provider.DisplayName} key removed. AI is now off.";
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            _providerStatus = $"The key could not be removed: {exception.Message}";
        }

        NotifyAll();
    }

    private async Task<AiSettings> CreateCloudSettingsAsync(string apiKey)
    {
        var provider = SelectedProvider;
        var model = ProviderEndpointPolicy.RequireModelId(CloudModel);

        // Copying a key from a web page easily brings a space or line break with it, and
        // the service then rejects a key that looks right on screen. Edges are trimmed; a
        // space inside is refused, because no key contains one and guessing which half was
        // meant (as with a pasted "Bearer ...") would be worse than asking.
        var key = apiKey.Trim();
        if (key.Any(char.IsWhiteSpace) || key.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                $"That key has a space in it. Copy only the key from {provider.KeySource}, without \"Bearer\" or anything else.");
        }

        // Each service keeps its own credential entry, so switching services never reuses
        // a key the user saved for a different company.
        if (key.Length > 0)
        {
            await credentialVault.SaveAsync(provider.CredentialReference, key);
            _keyWarning = provider.KeyPrefix is { } prefix && !key.StartsWith(prefix, StringComparison.Ordinal)
                ? $" {provider.DisplayName} keys usually start with \"{prefix}\", and this one does not. "
                    + "If AI ideas say the key was not accepted, check you copied the key for this service."
                : string.Empty;
        }
        else if (await credentialVault.RetrieveAsync(provider.CredentialReference) is null)
        {
            throw new InvalidOperationException($"Enter your {provider.DisplayName} key.");
        }

        return _loaded with
        {
            Mode = AiMode.Cloud,
            ProviderId = provider.Id,
            Endpoint = null,
            ModelId = model,
            CredentialReference = provider.CredentialReference,
            CloudConsentGranted = true,
            CloudDisclosures = Selected(),
        };
    }

    public IReadOnlyList<DisclosureCategory> PendingExpansions()
    {
        var selected = Selected();
        return selected.Where(category => !_loaded.CloudDisclosures.Contains(category)).ToArray();
    }

    public async Task SavePrivacyAsync()
    {
        _loaded = _loaded with { CloudDisclosures = Selected() };
        await settingsRepository.SaveAsync(_loaded);
        _saveStatus = "Saved on this computer. Nothing is sent unless you turn on online AI.";
        NotifyAll();
    }

    private HashSet<DisclosureCategory> Selected()
    {
        var selected = new HashSet<DisclosureCategory>();
        Add(ShareExtension, DisclosureCategory.Extension);
        Add(ShareMetadata, DisclosureCategory.Metadata);
        Add(ShareFileName, DisclosureCategory.FileName);
        Add(ShareFolderNames, DisclosureCategory.FolderNames);
        Add(ShareFullPath, DisclosureCategory.FullPath);
        return selected;

        void Add(bool enabled, DisclosureCategory category)
        {
            if (enabled)
            {
                selected.Add(category);
            }
        }
    }

    private void Apply(IReadOnlySet<DisclosureCategory> categories)
    {
        _shareExtension = categories.Contains(DisclosureCategory.Extension);
        _shareMetadata = categories.Contains(DisclosureCategory.Metadata);
        _shareFileName = categories.Contains(DisclosureCategory.FileName);
        _shareFolderNames = categories.Contains(DisclosureCategory.FolderNames);
        _shareFullPath = categories.Contains(DisclosureCategory.FullPath);
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(ShareExtension));
        OnPropertyChanged(nameof(ShareMetadata));
        OnPropertyChanged(nameof(ShareFileName));
        OnPropertyChanged(nameof(ShareFolderNames));
        OnPropertyChanged(nameof(ShareFullPath));
        OnPropertyChanged(nameof(AuthorizedFolderCount));
        OnPropertyChanged(nameof(SaveStatus));
        OnPropertyChanged(nameof(AiProcessing));
        OnPropertyChanged(nameof(InternetUse));
        OnPropertyChanged(nameof(CloudDataShared));
        OnPropertyChanged(nameof(SelectedModeIndex));
        OnPropertyChanged(nameof(LocalEndpoint));
        OnPropertyChanged(nameof(LocalModel));
        OnPropertyChanged(nameof(CloudModel));
        OnPropertyChanged(nameof(SelectedCloudProviderIndex));
        OnPropertyChanged(nameof(SelectedProviderName));
        OnPropertyChanged(nameof(CloudModelHint));
        OnPropertyChanged(nameof(CloudKeyHeader));
        OnPropertyChanged(nameof(CloudKeySourceHint));
        OnPropertyChanged(nameof(CloudConsentHeader));
        OnPropertyChanged(nameof(CloudProviderNote));
        OnPropertyChanged(nameof(RemoveKeyLabel));
        OnPropertyChanged(nameof(CloudConsent));
        OnPropertyChanged(nameof(ProviderStatus));
        OnPropertyChanged(nameof(TimeoutSeconds));
        OnPropertyChanged(nameof(DailyRequestLimit));
    }

    private static string FormatCategories(IEnumerable<DisclosureCategory> categories)
    {
        var names = categories.OrderBy(category => category).Select(category => category switch
        {
            DisclosureCategory.Extension => "file types",
            DisclosureCategory.Metadata => "file sizes and dates",
            DisclosureCategory.FileName => "file names",
            DisclosureCategory.FolderNames => "folder names",
            DisclosureCategory.FullPath => "full file locations",
            _ => "unknown information",
        }).ToArray();
        return names.Length == 0 ? "nothing" : string.Join(", ", names);
    }
}
