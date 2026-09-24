using DeskAI.App.ViewModels;
using DeskAI.Core.Studio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskAI.App.Views;

/// <summary>
/// Desktop Studio (ADR 0042). Its dialogs are the only places a person says yes: Connect, and
/// Send, which shows the exact list the AI would see. Everything else is the view model's.
/// </summary>
public sealed partial class DesktopStudioPage : Page
{
    public DesktopStudioPage(DesktopStudioViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        PageSizer.Attach(this, PageContent);
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    public DesktopStudioViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ViewModel.InitializeAsync();
    }

    private async void OnConnectClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsBusy && await PersonalFolderDialogs.ConfirmConnectAsync(XamlRoot, "Desktop"))
        {
            await ViewModel.ConnectDesktopAsync();
        }
    }

    private async void OnGuessClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsBusy)
        {
            await ViewModel.GuessAsync();
        }
    }

    /// <summary>Shows exactly what would be sent. Only Send sends; Cancel, Esc, and the X send nothing.</summary>
    private async void OnFindWithAiClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy || await ViewModel.PrepareAsync() is not { } question)
        {
            return;
        }

        if (await ConfirmSendAsync(question))
        {
            await ViewModel.SendAsync(question);
        }
    }

    private async Task<bool> ConfirmSendAsync(DesktopGroupQuestion question)
    {
        var content = new StackPanel { Spacing = 12, MaxWidth = 520 };
        content.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = $"{question.ServiceName} at {question.Destination} will see only this list: names and kinds of files. "
                + "Not what is inside them, and not where they are.",
        });
        var list = new StackPanel { Spacing = 4 };
        foreach (var line in question.Lines)
        {
            list.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        }

        content.Children.Add(new ScrollViewer
        {
            MaxHeight = 320,
            Content = list,
            Padding = new Thickness(12, 10, 12, 10),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DeskSurfaceBrush"],
        });
        if (question.LeftOut > 0)
        {
            content.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = $"{question.LeftOut} more will be sorted by DeskAI's own guess and not sent.",
            });
        }

        content.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = "The AI only suggests groups. It cannot move, rename, or open anything.",
        });
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Send this list to {question.ServiceName}?",
            Content = content,
            PrimaryButtonText = "Send",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string group } || ViewModel.IsBusy)
        {
            return;
        }

        var box = new TextBox { Header = "New name", Text = group, MaxLength = FolderNameMaxLength };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Rename {group}",
            Content = box,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.RenameGroupAsync(group, box.Text);
        }
    }

    private void OnMergeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string group } element || ViewModel.IsBusy)
        {
            return;
        }

        var menu = new MenuFlyout();
        foreach (var other in ViewModel.GroupNames.Where(name => name != group))
        {
            var item = new MenuFlyoutItem { Text = other };
            item.Click += async (_, _) => await ViewModel.MergeGroupAsync(group, other);
            menu.Items.Add(item);
        }

        if (menu.Items.Count > 0)
        {
            menu.ShowAt(element);
        }
    }

    private void OnMoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string path } element || ViewModel.IsBusy)
        {
            return;
        }

        var menu = new MenuFlyout();
        foreach (var name in ViewModel.GroupNames)
        {
            var item = new MenuFlyoutItem { Text = name };
            item.Click += async (_, _) => await ViewModel.MoveItemAsync(path, name);
            menu.Items.Add(item);
        }

        var notSure = new MenuFlyoutItem { Text = DesktopGroupBoard.NotSureName };
        notSure.Click += async (_, _) => await ViewModel.MoveItemAsync(path, null);
        menu.Items.Add(notSure);
        menu.ShowAt(element);
    }

    private const int FolderNameMaxLength = DeskAI.Core.Templates.FolderNameCheck.MaxNameLength;
}
