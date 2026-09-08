using DeskAI.App.ViewModels;
using DeskAI.Core.Ai;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskAI.App.Views;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
    }

    private async void OnSavePrivacyClick(object sender, RoutedEventArgs e)
    {
        var expansions = _viewModel.PendingExpansions();
        if (expansions.Count > 0)
        {
            var names = string.Join(", ", expansions.Select(DisplayName));
            var confirmation = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Share more information?",
                Content = $"You chose to allow: {names}. Nothing is sent while online AI is off. Private and protected files are always left out.",
                PrimaryButtonText = "Allow and save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        await _viewModel.SavePrivacyAsync();
    }

    private async void OnSaveProviderClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedModeIndex == (int)AiMode.Cloud)
        {
            var confirmation = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Turn on online AI?",
                Content = _viewModel.CloudConsentSummary(),
                PrimaryButtonText = "Turn on OpenRouter",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        await _viewModel.SaveProviderAsync(OpenRouterKeyBox.Password);
        OpenRouterKeyBox.Password = string.Empty;
    }

    private async void OnRemoveOpenRouterKeyClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.RemoveOpenRouterKeyAsync();
        OpenRouterKeyBox.Password = string.Empty;
    }

    private static string DisplayName(DisclosureCategory category) => category switch
    {
        DisclosureCategory.Extension => "file type",
        DisclosureCategory.Metadata => "file size and last changed date",
        DisclosureCategory.FileName => "file name",
        DisclosureCategory.FolderNames => "folder names",
        DisclosureCategory.FullPath => "full file location",
        DisclosureCategory.ExtractedContent => "text inside files",
        DisclosureCategory.ImageContent => "images inside files",
        _ => category.ToString(),
    };
}
