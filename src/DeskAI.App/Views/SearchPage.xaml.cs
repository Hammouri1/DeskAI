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
        var picked = await _folderPicker.PickFolderAsync(handle);

        // Cancelling is silent, because the person meant it. Anything else is said out
        // loud: pressing "Select folder" and seeing nothing happen looks like a broken app.
        if (picked.Outcome == FolderPickOutcome.Unavailable)
        {
            ViewModel.ReportFolderProblem(
                "Windows did not give DeskAI a location for that choice, so it could not be connected. "
                + "This happens with phones, cameras, and some cloud folders. Pick a folder on this computer.");
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
            Title = "Let DeskAI search this folder?",
            Content = $"{path}\n\nDeskAI will remember file names, sizes, and dates so you can search them. "
                + "It will not read what is inside your files unless you allow that separately afterwards, "
                + "and it cannot move, rename, or delete anything.\n\n"
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

    /// <summary>
    /// Grants or withdraws permission to read inside a folder's text files.
    /// </summary>
    /// <remarks>
    /// Granting asks first, and the question names exactly what will be opened, what will
    /// not, and what happens to what is read. Connecting a folder was consent to remember
    /// names, sizes, and dates; it was not consent to read what is written inside, so this
    /// is asked separately rather than folded into the first question.
    ///
    /// Withdrawing is not confirmed. Taking a permission back is never the direction that
    /// needs a second thought.
    /// </remarks>
    private async void OnContentPermissionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid rootId })
        {
            return;
        }

        var folder = ViewModel.Folders.FirstOrDefault(item => item.Id == rootId);
        if (folder is null)
        {
            return;
        }

        if (folder.CanReadContent)
        {
            await ViewModel.SetContentPermissionAsync(rootId, allow: false);
            return;
        }

        var confirmation = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Let DeskAI read inside these files?",
            Content = $"{folder.Path}\n\n"
                + "DeskAI will open plain text files here — notes, lists, and settings files — and read the "
                + "beginning of each one, so you can search for words written inside them.\n\n"
                + "It will not open PDFs, Word documents, spreadsheets, photos, or programs.\n"
                + "It still cannot move, rename, or delete anything.\n"
                + "What it reads is never saved and never sent anywhere.\n\n"
                + "You can turn this off at any time.",
            PrimaryButtonText = "Allow reading",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await confirmation.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.SetContentPermissionAsync(rootId, allow: true);
        }
    }
}
