using DeskAI.App.Services;
using DeskAI.App.ViewModels;
using DeskAI.Core.Ai;
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
        PageSizer.Attach(this, PageContent);
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

    /// <summary>
    /// Shows exactly the words that would be sent and to whom, and sends only if the person
    /// presses Send. The AI's reading lands in the box and is searched; cancelling sends nothing.
    /// </summary>
    private async void OnAskAiClick(object sender, RoutedEventArgs e)
    {
        var question = await ViewModel.PrepareAiReadingAsync();
        if (question is not null && await SentenceAiDialogs.ConfirmSendAsync(XamlRoot, question))
        {
            await ViewModel.AskAiToReadAsync(question);
        }
    }

    private async void OnSearchPicturesClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanSearchPictures)
        {
            return;
        }

        var scope = ViewModel.SelectedFolder.Id == Guid.Empty
            ? "all connected folders"
            : ViewModel.SelectedFolder.Name;
        var readDialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Read pictures for this search?",
            Content = $"DeskAI will open pictures in {scope}, including nested folders, to find “{ViewModel.Phrase}”. "
                + "This includes pictures inside PDFs and modern PowerPoint slides. "
                + "It checks at most 30 files and 12 pictures, up to 4 MB total. No image content is saved. "
                + (ViewModel.VisualAiName == "Local AI"
                    ? "The pictures will go only to your connected local AI on this computer."
                    : "Before any picture goes online, DeskAI will show the exact list and ask again."),
            PrimaryButtonText = "Read pictures",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await readDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var batch = await ViewModel.PreparePictureSearchAsync(visualReadApproved: true);
        if (batch is null)
        {
            return;
        }

        if (batch.Mode == AiMode.Cloud)
        {
            var list = string.Join("\n", batch.Images.Select(image =>
                $"• {image.FileName} — {image.Location} ({image.Bytes.Length / 1024d:0.#} KB)"));
            var sendDialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = $"Send {batch.Images.Count} pictures to {batch.ServiceName}?",
                Content = new ScrollViewer
                {
                    MaxHeight = 360,
                    Content = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = $"Destination: {batch.Destination}\n\nThese exact image bytes and your description will be sent. "
                            + "Names and folder paths are shown here for your choice; they are not included in the AI request. "
                            + "Your provider may charge for each picture. The total image data is "
                            + $"{batch.Images.Sum(image => image.Bytes.Length) / 1024d:0.#} KB. "
                            + "This permission applies only to this search.\n\n" + list,
                    },
                },
                PrimaryButtonText = "Send these pictures",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await sendDialog.ShowAsync() != ContentDialogResult.Primary)
            {
                await ViewModel.SearchPicturesAsync(batch, cloudSendApproved: false);
                return;
            }
        }

        await ViewModel.SearchPicturesAsync(batch, cloudSendApproved: batch.Mode == AiMode.Cloud);
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
                + "and connecting never lets it move, rename, or delete anything. Tidying is a separate yes you give on Organize.\n\n"
                + "You can disconnect this folder at any time, which immediately forgets everything remembered about it.",
            PrimaryButtonText = "Connect folder",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await confirmation.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.ConnectFolderAsync(path);
            await RefreshScopeReminderAsync();
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
            await RefreshScopeReminderAsync();
            return;
        }

        if (await ConfirmDocumentReadingAsync(folder))
        {
            await ViewModel.SetContentPermissionAsync(rootId, allow: true);
            await RefreshScopeReminderAsync();
        }
    }

    private async void OnDocumentsPermissionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid rootId }
            && ViewModel.Folders.FirstOrDefault(item => item.Id == rootId) is { } folder
            && folder.CanUpgradeDocuments
            && await ConfirmDocumentReadingAsync(folder))
        {
            await ViewModel.SetContentPermissionAsync(rootId, allow: true);
            await RefreshScopeReminderAsync();
        }
    }

    private async void OnPdfPermissionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid rootId }
            || ViewModel.Folders.FirstOrDefault(item => item.Id == rootId) is not { } folder)
        {
            return;
        }

        if (folder.CanReadPdf)
        {
            await ViewModel.SetPdfPermissionAsync(rootId, allow: false);
            return;
        }

        if (!folder.CanUpgradePdf)
        {
            return;
        }

        var confirmation = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Search text inside PDFs here?",
            Content = $"{folder.Path}\n\nDeskAI will read PDF text on this computer when you search. "
                + "It checks up to 50 files per search, 8 MB and the first 20 pages per PDF. "
                + "Scanned pages and photos are not read. Some PDFs may be skipped or only partly read.\n\n"
                + "The words are not saved or sent to AI. You can stop PDF reading at any time.",
            PrimaryButtonText = "Allow PDF reading",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirmation.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.SetPdfPermissionAsync(rootId, allow: true);
        }
    }

    private async void OnSlidesPermissionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid rootId }
            || ViewModel.Folders.FirstOrDefault(item => item.Id == rootId) is not { } folder)
        {
            return;
        }

        if (folder.CanReadSlides)
        {
            await ViewModel.SetSlidesPermissionAsync(rootId, allow: false);
            return;
        }

        if (!folder.CanUpgradeSlides)
        {
            return;
        }

        var confirmation = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Search text on PowerPoint slides here?",
            Content = $"{folder.Path}\n\nDeskAI will read text on modern PowerPoint (.pptx) slides on this computer when you search. "
                + "It checks up to 50 files per search, 8 MB and the first 40 slides per presentation. "
                + "Pictures, embedded objects, and older PowerPoint files stay closed. Some presentations may be skipped or partly read.\n\n"
                + "The words are not saved or sent to AI. You can stop PowerPoint reading at any time.",
            PrimaryButtonText = "Allow PowerPoint reading",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirmation.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.SetSlidesPermissionAsync(rootId, allow: true);
        }
    }

    private static Task RefreshScopeReminderAsync() =>
        ((App)Application.Current).MainAppWindow is MainWindow window
            ? window.RefreshScopeAsync()
            : Task.CompletedTask;

    private async Task<bool> ConfirmDocumentReadingAsync(ConnectedFolderViewModel folder)
    {
        var confirmation = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Search inside this folder's documents?",
            Content = $"{folder.Path}\n\n"
                + "DeskAI will read the beginning of up to 50 files per search: plain-text notes, modern Word (.docx), and Excel (.xlsx). "
                + "It will not open PDFs, PowerPoint slides, photos, older Office files, or programs.\n\n"
                + "Reading happens on this computer. The words are not saved or sent to an AI service. "
                + "This does not let DeskAI move, rename, or delete files. You can turn it off whenever you like.",
            PrimaryButtonText = "Allow reading",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        return await confirmation.ShowAsync() == ContentDialogResult.Primary;
    }
}
