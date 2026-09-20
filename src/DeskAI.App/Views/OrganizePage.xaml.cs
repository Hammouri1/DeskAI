using DeskAI.App.Services;
using DeskAI.App.ViewModels;
using DeskAI.Core.Tidy;
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

    public OrganizePage(TidyViewModel viewModel, IFolderPickerService folderPicker)
    {
        InitializeComponent();
        PageSizer.Attach(this, PageContent);
        ViewModel = viewModel;
        _folderPicker = folderPicker;
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
        if (ViewModel.SelectedFolder is { } folder && await ConfirmTidyPermissionAsync(folder))
        {
            await ViewModel.AllowTidyAsync();
        }
    }

    /// <summary>
    /// The away switch. On opens the dialog that names the rules and the ceiling, and only its
    /// yes records anything; off needs no dialog, because taking a permission back never does.
    /// </summary>
    private async void OnAwayToggled(object sender, RoutedEventArgs e)
    {
        if (AwaySwitch.IsOn == ViewModel.IsAwayOn)
        {
            // The binding just moved the switch to match the stored state; nothing to do.
            return;
        }

        if (!AwaySwitch.IsOn)
        {
            await ViewModel.TurnAwayOffAsync();
            return;
        }

        var question = await ViewModel.PrepareAwayQuestionAsync();
        if (question is null || !await ConfirmAwayAsync(question))
        {
            AwaySwitch.IsOn = ViewModel.IsAwayOn;
            return;
        }

        await ViewModel.TurnAwayOnAsync();
        AwaySwitch.IsOn = ViewModel.IsAwayOn;
    }

    private async Task<bool> ConfirmAwayAsync(AwayTidyQuestion question)
    {
        var content = new StackPanel { Spacing = 12, MaxWidth = 480 };
        content.Children.Add(new TextBlock { Text = question.Intro, TextWrapping = TextWrapping.Wrap });
        foreach (var rule in question.Rules)
        {
            content.Children.Add(new TextBlock
            {
                Text = "• " + rule,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
        }

        content.Children.Add(new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(0, 4, 4, 0),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DeskSurfaceBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DeskAccentBrush"],
            Child = new TextBlock { Text = question.Promise, TextWrapping = TextWrapping.Wrap },
        });

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = question.Title,
            Content = content,
            PrimaryButtonText = "Tidy while I'm away",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            PrimaryButtonStyle = (Style)Application.Current.Resources["AccentButtonStyle"],
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>
    /// Undo moves files too, so after the tidy permission was taken back it asks for it again,
    /// with the same dialog, before trying once more.
    /// </summary>
    private async void OnUndoClick(object sender, RoutedEventArgs e)
    {
        var result = await ViewModel.UndoLastTidyAsync();
        if (result is { NeedsPermission: true } &&
            ViewModel.SelectedFolder is { } folder &&
            await ConfirmTidyPermissionAsync(folder))
        {
            await ViewModel.AllowTidyAsync();
            await ViewModel.UndoLastTidyAsync();
        }
    }

    /// <summary>
    /// "Undo those" after an interrupted tidy moves files back, so it asks for the tidy
    /// permission the same way Undo does when that was taken back.
    /// </summary>
    private async void OnUndoInterruptedClick(object sender, RoutedEventArgs e)
    {
        var result = await ViewModel.UndoInterruptedAsync();
        if (result is { NeedsPermission: true } &&
            ViewModel.SelectedFolder is { } folder &&
            await ConfirmTidyPermissionAsync(folder))
        {
            await ViewModel.AllowTidyAsync();
            await ViewModel.UndoInterruptedAsync();
        }
    }

    private async Task<bool> ConfirmTidyPermissionAsync(TidyFolderOption folder)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Allow DeskAI to tidy {folder.Name}?",
            Content = $"{folder.Path}\n\nDeskAI may move loose files at the top of this folder into folders inside it.\n"
                + "It never deletes anything, never touches files in subfolders, and never moves anything out of this folder.\n"
                + "Nothing moves until you press Tidy or Undo.\n\nYou can take this back at any time.",
            PrimaryButtonText = "Allow tidying",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await confirm.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>
    /// Shows exactly what the AI would see about each file and where it would go, and sends
    /// only if the person presses Send. Cancelling sends nothing.
    /// </summary>
    private async void OnAskAiClick(object sender, RoutedEventArgs e)
    {
        var question = await ViewModel.PrepareAiQuestionAsync();
        if (question is not null && await ConfirmSendAsync(question))
        {
            await ViewModel.AskAiAsync(question);
        }
    }

    /// <summary>
    /// The same dialog as Ask AI, with one more line: the AI may name folders, each checked by
    /// DeskAI and only ever inside this folder. Sends only if the person presses Send.
    /// </summary>
    private async void OnPlanAiClick(object sender, RoutedEventArgs e)
    {
        var question = await ViewModel.PreparePlanQuestionAsync();
        if (question is not null && await ConfirmSendAsync(question))
        {
            await ViewModel.AskAiAsync(question);
        }
    }

    private async Task<bool> ConfirmSendAsync(TidyAiQuestion question)
    {
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
        if (question.IsPlan)
        {
            content.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "It may also suggest folder names. DeskAI checks every name and only ever makes folders inside this one; "
                    + "you see the whole plan before anything moves.",
            });
        }

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Send this to {question.ServiceName}?",
            Content = content,
            PrimaryButtonText = "Send",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await confirm.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task ShowAsync(string title, string content) =>
        await new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = content, CloseButtonText = "OK" }.ShowAsync();
}
