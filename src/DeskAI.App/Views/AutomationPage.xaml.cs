using DeskAI.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskAI.App.Views;

public sealed partial class AutomationPage : Page
{
    public AutomationPage(AutomationViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public AutomationViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ViewModel.InitializeAsync();
    }

    /// <summary>
    /// Leaving Automatic tasks stops this page's copy from listening to the icon's singleton
    /// controller. Without this, a fresh, still-subscribed view model would pile up on every
    /// visit to this page.
    /// </summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        ViewModel.Dispose();
    }

    /// <summary>
    /// Shows exactly the words that would be sent and to whom, and sends only if the person
    /// presses Send. The AI's reading fills the boxes below; nothing is saved by it.
    /// </summary>
    private async void OnAskAiClick(object sender, RoutedEventArgs e)
    {
        var question = await ViewModel.PrepareAiDraftAsync();
        if (question is not null && await SentenceAiDialogs.ConfirmSendAsync(XamlRoot, question))
        {
            await ViewModel.AskAiToDraftAsync(question);
        }
    }

    /// <summary>
    /// Turning this on asks before it does anything, because it changes what closing the
    /// window means. Turning it off needs no dialog: stopping is always safe.
    /// </summary>
    private async void OnKeepRunningToggled(object sender, RoutedEventArgs args)
    {
        if (ViewModel is null || KeepRunningSwitch.IsOn == ViewModel.KeepsRunningWhenClosed)
        {
            // The switch is only reflecting a change the view model already made.
            return;
        }

        if (!KeepRunningSwitch.IsOn)
        {
            await ViewModel.StopKeepingRunningAsync();
            return;
        }

        var question = ViewModel.AskAboutKeepingRunning();
        var notify = new CheckBox
        {
            Content = question.NotifyLabel,
            IsChecked = question.NotifyWhenSomethingIsFound,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = question.Title,
            PrimaryButtonText = question.Confirm,
            CloseButtonText = question.Decline,
            DefaultButton = ContentDialogButton.Close,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = question.Body, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = question.LimitLine, TextWrapping = TextWrapping.Wrap },
                    notify,
                    new TextBlock
                    {
                        Text = question.NotifyCaption,
                        TextWrapping = TextWrapping.Wrap,
                        Style = (Style)Application.Current.Resources["CaptionStyle"],
                    },
                },
            },
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.KeepRunningAsync(notify.IsChecked == true);
        }
        else
        {
            // Put the switch back where it was. Closing the dialog is a decision not to.
            KeepRunningSwitch.IsOn = false;
        }
    }
}
