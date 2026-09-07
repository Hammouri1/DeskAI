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
        var path = await _folderPicker.PickFolderAsync(handle);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

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
