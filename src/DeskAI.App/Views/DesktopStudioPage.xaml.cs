using DeskAI.App.ViewModels;
using DeskAI.Core.Studio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskAI.App.Views;

/// <summary>
/// Desktop Studio (ADR 0042, ADR 0044). Its dialogs are the only places a person says yes: Connect;
/// Send, which shows the exact list the AI would see; and the one-time yes to move things on the
/// Desktop. Everything else is the view model's.
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

    /// <summary>
    /// Rename and Merge share one small menu so the group name keeps the width of its card.
    /// Merge into is left out when there is no other group to merge into.
    /// </summary>
    private void OnGroupOptionsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string group } element || ViewModel.IsBusy)
        {
            return;
        }

        var menu = new MenuFlyout();
        var rename = new MenuFlyoutItem { Text = "Rename…", Icon = new FontIcon { Glyph = "" } };
        rename.Click += async (_, _) => await RenameAsync(group);
        menu.Items.Add(rename);

        var merge = new MenuFlyoutSubItem { Text = "Merge into", Icon = new FontIcon { Glyph = "" } };
        foreach (var other in ViewModel.GroupNames.Where(name => name != group))
        {
            var item = new MenuFlyoutItem { Text = other };
            item.Click += async (_, _) => await ViewModel.MergeGroupAsync(group, other);
            merge.Items.Add(item);
        }

        if (merge.Items.Count > 0)
        {
            menu.Items.Add(merge);
        }

        menu.ShowAt(element);
    }

    private async Task RenameAsync(string group)
    {
        if (ViewModel.IsBusy)
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

    private DesktopMoveCardViewModel? CardOf(object sender) => (sender as FrameworkElement)?.Tag switch
    {
        "OldStuff" => ViewModel.OldStuff,
        "FolderByGroup" => ViewModel.FolderByGroup,
        "TagNames" => ViewModel.TagNames,
        _ => null,
    };

    private async void OnPreviewClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsBusy && CardOf(sender) is { } card)
        {
            await ViewModel.PreviewAsync(card);
        }
    }

    /// <summary>The first Move asks for the yes, then tries once more with the same ticked list.</summary>
    private async void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy || CardOf(sender) is not { } card)
        {
            return;
        }

        if (await ViewModel.ApplyAsync(card) is { NeedsPermission: true } && await ConfirmMovingAsync())
        {
            await ViewModel.AllowMovingAsync();
            await ViewModel.ApplyAsync(card);
        }
    }

    /// <summary>Put back moves things too, so after the yes was taken back it asks again first.</summary>
    private async void OnPutBackClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy || CardOf(sender) is not { } card)
        {
            return;
        }

        if (await ViewModel.PutBackAsync(card) is { NeedsPermission: true } && await ConfirmMovingAsync())
        {
            await ViewModel.AllowMovingAsync();
            await ViewModel.PutBackAsync(card);
        }
    }

    private async void OnPutBackInterruptedClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy)
        {
            return;
        }

        if (await ViewModel.PutBackInterruptedAsync() is { NeedsPermission: true } && await ConfirmMovingAsync())
        {
            await ViewModel.AllowMovingAsync();
            await ViewModel.PutBackInterruptedAsync();
        }
    }

    private async void OnKeepInterruptedClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsBusy)
        {
            await ViewModel.KeepInterruptedAsync();
        }
    }

    /// <summary>Taking the yes back needs no dialog; that is never the dangerous direction.</summary>
    private async void OnStopMovingClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsBusy)
        {
            await ViewModel.StopMovingAsync();
        }
    }

    private async Task<bool> ConfirmMovingAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Allow DeskAI to move or rename things on your Desktop?",
            Content = "DeskAI may move folders and files into folders on your Desktop, and rename folders there: only the ones you tick, and only when you press Move or Rename.\n"
                + "It never deletes anything and never moves anything off your Desktop.\n"
                + "Put back returns your latest change.\n\nYou can take this back at any time.",
            PrimaryButtonText = "Allow",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private const int FolderNameMaxLength = DeskAI.Core.Templates.FolderNameCheck.MaxNameLength;
}
