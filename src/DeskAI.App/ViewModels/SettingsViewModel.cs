using CommunityToolkit.Mvvm.ComponentModel;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;

namespace DeskAI.App.ViewModels;

public sealed class SettingsViewModel(
    IAiSettingsRepository settingsRepository,
    IAuthorizedRootRepository rootRepository) : ObservableObject
{
    private AiSettings _loaded = AiSettings.Default;
    private bool _shareExtension = true;
    private bool _shareMetadata;
    private bool _shareFileName;
    private bool _shareFolderNames;
    private bool _shareFullPath;
    private string _authorizedFolderCount = "0";
    private string _saveStatus = "AI is off. These choices set the maximum data a future cloud request may use.";

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

    public async Task InitializeAsync()
    {
        _loaded = await settingsRepository.LoadAsync();
        Apply(_loaded.CloudDisclosures);
        _authorizedFolderCount = (await rootRepository.ListAsync()).Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
        NotifyAll();
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
    }

    private static string FormatCategories(IEnumerable<DisclosureCategory> categories) =>
        string.Join(", ", categories.OrderBy(category => category).Select(category => category.ToString()));
}
