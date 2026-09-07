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
                Title = "Allow more data categories?",
                Content = $"You are allowing: {names}. Nothing is sent while AI is off, and protected files remain excluded.",
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
                Title = "Enable Google Gemini?",
                Content = _viewModel.CloudConsentSummary(),
                PrimaryButtonText = "Enable Gemini",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        await _viewModel.SaveProviderAsync(GeminiKeyBox.Password);
        GeminiKeyBox.Password = string.Empty;
    }

    private async void OnRemoveGeminiKeyClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.RemoveGeminiKeyAsync();
        GeminiKeyBox.Password = string.Empty;
    }

    private static string DisplayName(DisclosureCategory category) => category switch
    {
        DisclosureCategory.Extension => "file extension",
        DisclosureCategory.Metadata => "size and modified date",
        DisclosureCategory.FileName => "file name",
        DisclosureCategory.FolderNames => "folder names",
        DisclosureCategory.FullPath => "full path",
        DisclosureCategory.ExtractedContent => "document contents",
        DisclosureCategory.ImageContent => "image contents",
        _ => category.ToString(),
    };
}
