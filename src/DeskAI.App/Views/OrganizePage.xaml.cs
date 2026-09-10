using DeskAI.App.Navigation;
using DeskAI.App.Services;
using DeskAI.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskAI.App.Views;

/// <summary>
/// "Tidy a folder": pick a folder, allow tidying, and see what would happen.
/// </summary>
/// <remarks>
/// The two questions that change what DeskAI may do — connecting a folder, and allowing it to
/// be tidied — are asked here in dialogs that name the folder and say exactly what follows.
/// Everything else is in <see cref="TidyViewModel"/>, where it is tested.
/// </remarks>
public sealed partial class OrganizePage : Page
{
    private readonly IFolderPickerService _folderPicker;
    private readonly INavigationService _navigation;

    public OrganizePage(TidyViewModel viewModel, IFolderPickerService folderPicker, INavigationService navigation)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _folderPicker = folderPicker;
        _navigation = navigation;
        Loaded += OnLoaded;
    }

    public TidyViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ViewModel.InitializeAsync();
    }

    private async void OnChooseFolderClick(object sender, RoutedEventArgs e)
    {
        var window = ((App)Application.Current).MainAppWindow;
        if (window is null)
        {
            return;
        }

        var picked = await _folderPicker.PickFolderAsync(WinRT.Interop.WindowNative.GetWindowHandle(window));

        // Cancelling is silent because the person meant it. A choice Windows gave no location
        // for is said out loud, or the button would look broken.
        if (picked.Outcome == FolderPickOutcome.Unavailable)
        {
            await ShowAsync(
                "That folder could not be used",
                "Windows did not give DeskAI a location for that choice. This happens with phones, cameras, "
                    + "and some cloud folders. Pick a folder on this computer.");
            return;
        }

        if (!picked.WasPicked)
        {
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Let DeskAI look at this folder?",
            Content = $"{picked.Path}\n\nDeskAI will remember file names, sizes, and dates. It will not read inside "
                + "your files, and it will not move anything unless you allow tidying next.",
            PrimaryButtonText = "Connect folder",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.ConnectAndSelectAsync(picked.Path!);
        }
    }

    private async void OnAllowTidyClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedFolder is not { } folder)
        {
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Allow DeskAI to tidy {folder.Name}?",
            Content = $"{folder.Path}\n\nDeskAI may move loose files at the top of this folder into folders inside it.\n"
                + "It never deletes anything, never touches files in subfolders, and never moves anything out of this folder.\n"
                + "Nothing moves until you press Tidy.\n\nYou can take this back at any time.",
            PrimaryButtonText = "Allow tidying",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.AllowTidyAsync();
        }
    }

    /// <summary>
    /// Shows exactly what the AI would see about each file and where it would go, and sends
    /// only if the person presses Send. Cancelling sends nothing.
    /// </summary>
    private async void OnAskAiClick(object sender, RoutedEventArgs e)
    {
        var question = await ViewModel.PrepareAiQuestionAsync();
        if (question is null)
        {
            return;
        }

        var files = new StackPanel { Spacing = 4 };
        foreach (var line in question.FileLines)
        {
            files.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap });
        }

        var content = new StackPanel { Spacing = 12, MaxWidth = 480 };
        content.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = $"{question.ServiceName}, at {question.Destination}, will see this about each file, and nothing else:",
        });
        content.Children.Add(new ScrollViewer { MaxHeight = 240, Content = files });
        if (question.LeftOutCount > 0)
        {
            content.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = question.LeftOutCount == 1
                    ? "1 more file is not included this time."
                    : $"{question.LeftOutCount} more files are not included this time.",
            });
        }

        content.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = "Each file also gets a random number so DeskAI can match the answers. What is inside your files, "
                + "where they are on your computer, and folder names are never sent. AI only suggests; nothing moves.",
        });

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Send this to {question.ServiceName}?",
            Content = content,
            PrimaryButtonText = "Send",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.AskAiAsync(question);
        }
    }

    private void OnPracticeClick(object sender, RoutedEventArgs e) => _navigation.Navigate("practice");

    private async Task ShowAsync(string title, string content) =>
        await new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = content, CloseButtonText = "OK" }.ShowAsync();
}
