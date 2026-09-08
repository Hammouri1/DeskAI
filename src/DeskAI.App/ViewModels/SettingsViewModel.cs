using CommunityToolkit.Mvvm.ComponentModel;
using DeskAI.AI;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;

namespace DeskAI.App.ViewModels;

public sealed class SettingsViewModel(
    IAiSettingsRepository settingsRepository,
    IAuthorizedRootRepository rootRepository,
    ICredentialVault credentialVault) : ObservableObject
{
    private const string OpenRouterCredentialReference = "DeskAI/OpenRouter";
    private AiSettings _loaded = AiSettings.Default;
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
    private string _openRouterModel = string.Empty;
    private bool _cloudConsent;
    private string _providerStatus = "Choose whether you want to use AI.";
    private double _timeoutSeconds = 30;
    private double _dailyRequestLimit = 20;

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
        AiMode.Cloud => "Online with OpenRouter",
        _ => "AI is off",
    };
    public string InternetUse => _loaded.Mode == AiMode.Cloud ? "On — OpenRouter" : "Off";
    public string CloudDataShared => _loaded.Mode == AiMode.Cloud
        ? FormatCategories(_loaded.CloudDisclosures)
        : "None — AI is off";
    public int SelectedModeIndex { get => _selectedModeIndex; set => SetProperty(ref _selectedModeIndex, value); }
    public string LocalEndpoint { get => _localEndpoint; set => SetProperty(ref _localEndpoint, value); }
    public string LocalModel { get => _localModel; set => SetProperty(ref _localModel, value); }
    public string OpenRouterModel { get => _openRouterModel; set => SetProperty(ref _openRouterModel, value); }
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
        _openRouterModel = _loaded.ProviderId == "openrouter" ? _loaded.ModelId : string.Empty;
        _cloudConsent = _loaded.CloudConsentGranted;
        _timeoutSeconds = _loaded.TimeoutSeconds;
        _dailyRequestLimit = _loaded.DailyRequestLimit;
        _authorizedFolderCount = (await rootRepository.ListAsync()).Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
        NotifyAll();
    }

    public string CloudConsentSummary() =>
        $"OpenRouter will receive only: {FormatCategories(Selected())}. File contents and protected files are always left out.";

    public async Task SaveProviderAsync(string apiKey)
    {
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
                AiMode.Cloud when CloudConsent => await CreateOpenRouterSettingsAsync(apiKey),
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
                _ => "Saved. OpenRouter is ready with your sharing choices.",
            };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _providerStatus = $"Settings were not enabled: {exception.Message}";
        }

        NotifyAll();
    }

    public async Task RemoveOpenRouterKeyAsync()
    {
        try
        {
            await credentialVault.RemoveAsync(OpenRouterCredentialReference);
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
            _providerStatus = "OpenRouter key removed. AI is now off.";
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            _providerStatus = $"The key could not be removed: {exception.Message}";
        }

        NotifyAll();
    }

    private async Task<AiSettings> CreateOpenRouterSettingsAsync(string apiKey)
    {
        var model = ProviderEndpointPolicy.RequireModelId(OpenRouterModel);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            await credentialVault.SaveAsync(OpenRouterCredentialReference, apiKey);
        }
        else if (await credentialVault.RetrieveAsync(OpenRouterCredentialReference) is null)
        {
            throw new InvalidOperationException("Enter your OpenRouter key.");
        }

        return _loaded with
        {
            Mode = AiMode.Cloud,
            ProviderId = "openrouter",
            Endpoint = null,
            ModelId = model,
            CredentialReference = OpenRouterCredentialReference,
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
        OnPropertyChanged(nameof(OpenRouterModel));
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
