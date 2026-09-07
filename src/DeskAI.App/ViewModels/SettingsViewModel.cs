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
    private const string GeminiCredentialReference = "DeskAI/Gemini";
    private AiSettings _loaded = AiSettings.Default;
    private bool _shareExtension = true;
    private bool _shareMetadata;
    private bool _shareFileName;
    private bool _shareFolderNames;
    private bool _shareFullPath;
    private string _authorizedFolderCount = "0";
    private string _saveStatus = "AI is off. These choices set the maximum data a future cloud request may use.";
    private int _selectedModeIndex;
    private string _localEndpoint = string.Empty;
    private string _localModel = string.Empty;
    private string _geminiModel = string.Empty;
    private bool _cloudConsent;
    private string _providerStatus = "Choose Rule Engine Only, local AI, or Gemini.";

    public bool ShareExtension { get => _shareExtension; set => SetProperty(ref _shareExtension, value); }
    public bool ShareMetadata { get => _shareMetadata; set => SetProperty(ref _shareMetadata, value); }
    public bool ShareFileName { get => _shareFileName; set => SetProperty(ref _shareFileName, value); }
    public bool ShareFolderNames { get => _shareFolderNames; set => SetProperty(ref _shareFolderNames, value); }
    public bool ShareFullPath { get => _shareFullPath; set => SetProperty(ref _shareFullPath, value); }
    public string AuthorizedFolderCount => _authorizedFolderCount;
    public string SaveStatus => _saveStatus;
    public string AiProcessing => _loaded.Mode switch
    {
        AiMode.Local => "Local AI",
        AiMode.Cloud => $"Cloud · {_loaded.ProviderId}",
        _ => "Rule Engine Only",
    };
    public string InternetUse => _loaded.Mode == AiMode.Cloud ? $"On · {_loaded.ProviderId}" : "Off";
    public string CloudDataShared => _loaded.Mode == AiMode.Cloud
        ? FormatCategories(_loaded.CloudDisclosures)
        : "None — AI is off";
    public int SelectedModeIndex { get => _selectedModeIndex; set => SetProperty(ref _selectedModeIndex, value); }
    public string LocalEndpoint { get => _localEndpoint; set => SetProperty(ref _localEndpoint, value); }
    public string LocalModel { get => _localModel; set => SetProperty(ref _localModel, value); }
    public string GeminiModel { get => _geminiModel; set => SetProperty(ref _geminiModel, value); }
    public bool CloudConsent { get => _cloudConsent; set => SetProperty(ref _cloudConsent, value); }
    public string ProviderStatus => _providerStatus;

    public async Task InitializeAsync()
    {
        _loaded = await settingsRepository.LoadAsync();
        Apply(_loaded.CloudDisclosures);
        _selectedModeIndex = (int)_loaded.Mode;
        _localEndpoint = _loaded.Mode == AiMode.Local ? _loaded.Endpoint ?? string.Empty : string.Empty;
        _localModel = _loaded.Mode == AiMode.Local ? _loaded.ModelId : string.Empty;
        _geminiModel = _loaded.ProviderId == "gemini" ? _loaded.ModelId : string.Empty;
        _cloudConsent = _loaded.CloudConsentGranted;
        _authorizedFolderCount = (await rootRepository.ListAsync()).Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
        NotifyAll();
    }

    public string CloudConsentSummary() =>
        $"Google Gemini will receive only: {FormatCategories(Selected())}. File contents and protected files are excluded. Provider pricing, retention, and availability are controlled by Google.";

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
                AiMode.Cloud when CloudConsent => await CreateGeminiSettingsAsync(apiKey),
                _ => throw new InvalidOperationException("Confirm cloud sharing before enabling Gemini."),
            };

            await settingsRepository.SaveAsync(updated);
            _loaded = updated;
            _providerStatus = mode switch
            {
                AiMode.RuleEngineOnly => "Saved. DeskAI will use deterministic rules only.",
                AiMode.Local => "Saved. Only the configured local loopback endpoint may be used.",
                _ => "Saved. Gemini is enabled with your confirmed sharing limits.",
            };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _providerStatus = $"Settings were not enabled: {exception.Message}";
        }

        NotifyAll();
    }

    public async Task RemoveGeminiKeyAsync()
    {
        try
        {
            await credentialVault.RemoveAsync(GeminiCredentialReference);
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
            _providerStatus = "Gemini key removed. Rule Engine Only mode is active.";
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            _providerStatus = $"The key could not be removed: {exception.Message}";
        }

        NotifyAll();
    }

    private async Task<AiSettings> CreateGeminiSettingsAsync(string apiKey)
    {
        var model = ProviderEndpointPolicy.RequireModelId(GeminiModel);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            await credentialVault.SaveAsync(GeminiCredentialReference, apiKey);
        }
        else if (await credentialVault.RetrieveAsync(GeminiCredentialReference) is null)
        {
            throw new InvalidOperationException("Enter a Gemini API key.");
        }

        return _loaded with
        {
            Mode = AiMode.Cloud,
            ProviderId = "gemini",
            Endpoint = null,
            ModelId = model,
            CredentialReference = GeminiCredentialReference,
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
        _saveStatus = "Privacy choices saved locally. AI remains off until you explicitly select a provider.";
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
        OnPropertyChanged(nameof(GeminiModel));
        OnPropertyChanged(nameof(CloudConsent));
        OnPropertyChanged(nameof(ProviderStatus));
    }

    private static string FormatCategories(IEnumerable<DisclosureCategory> categories) =>
        string.Join(", ", categories.OrderBy(category => category).Select(category => category.ToString()));
}
