using DeskAI.App.Services;
using DeskAI.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace DeskAI.App.Views;

public sealed partial class SearchPage : Page
{
    private readonly IFolderPickerService _folderPicker;

    public SearchPage(SearchViewModel viewModel, IFolderPickerService folderPicker)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _folderPicker = folderPicker;
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    public SearchViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ViewModel.InitializeAsync();
    }

    /// <summary>Enter searches, because reaching for the mouse to run a search is friction.</summary>
    private void OnPhraseKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not Windows.System.VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        if (ViewModel.SearchCommand.CanExecute(null))
        {
            ViewModel.SearchCommand.Execute(null);
        }
    }

    /// <summary>Asks for a name, then saves the phrase currently in the box.</summary>
    private async void OnSaveSearchClick(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox
        {
            PlaceholderText = "University photos",
            MaxLength = SearchViewModel.MaxSavedSearchNameLength,
        };
        AutomationProperties.SetName(nameBox, "Name for this saved search");

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Save this search",
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = $"DeskAI will remember the words \"{ViewModel.Phrase}\" so you can run them again. "
                            + "A saved search finds files. It never moves, renames, or deletes anything.",
                    },
                    nameBox,
                },
            },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.SaveCurrentSearchAsync(nameBox.Text);
        }
    }

    /// <summary>
    /// Picks a folder through the trusted Windows dialog and confirms it before anything is
    /// read.
    /// </summary>
    /// <remarks>
    /// The confirmation states the exact path and the exact limits, because authorizing a
    /// folder is the moment a person decides what DeskAI may look at. Nothing is read until
    /// they choose the primary button.
    /// </remarks>
    private async void OnConnectFolderClick(object sender, RoutedEventArgs e)
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
            Title = "Let DeskAI search this folder?",
            Content = $"{path}\n\nDeskAI will remember file names, sizes, and dates so you can search them. "
                + "It will not read what is inside your files, and it cannot move, rename, or delete anything.\n\n"
                + "You can disconnect this folder at any time, which immediately forgets everything remembered about it.",
            PrimaryButtonText = "Connect folder",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await confirmation.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.ConnectFolderAsync(path);
        }
    }
}
