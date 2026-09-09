using DeskAI.App.ViewModels;
using DeskAI.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskAI.App.Views;

public sealed partial class OrganizePage : Page
{
    private readonly OrganizeViewModel _viewModel;
    private readonly IFolderPickerService _folderPicker;

    public OrganizePage(OrganizeViewModel viewModel, IFolderPickerService folderPicker)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _folderPicker = folderPicker;
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        _viewModel.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
    }

    private async void OnChooseFolderClick(object sender, RoutedEventArgs e)
    {
        var window = ((App)Application.Current).MainAppWindow;
        if (window is null)
        {
            return;
        }

        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var picked = await _folderPicker.PickFolderAsync(handle);

        // Cancelling is silent; a folder Windows gave no location for is not, or the button
        // would look broken.
        if (picked.Outcome == FolderPickOutcome.Unavailable)
        {
            await new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "That folder could not be used",
                Content = "Windows did not give DeskAI a location for that choice, so there is nothing to "
                    + "preview. This happens with phones, cameras, and some cloud folders. "
                    + "Pick a folder on this computer.",
                CloseButtonText = "OK",
            }.ShowAsync();
            return;
        }

        if (!picked.WasPicked)
        {
            return;
        }

        var path = picked.Path!;
        var confirmation = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Preview this folder?",
            Content = $"{path}\n\nDeskAI will read file names, sizes, and dates only. It cannot move, rename, delete, or read file contents.",
            PrimaryButtonText = "Allow read-only preview",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await confirmation.ShowAsync() == ContentDialogResult.Primary)
        {
            await _viewModel.PreviewFolderAsync(path);
        }
    }
}
