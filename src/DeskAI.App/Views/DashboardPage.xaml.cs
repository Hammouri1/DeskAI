using DeskAI.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskAI.App.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public DashboardViewModel ViewModel { get; }

    /// <summary>
    /// Read when the page opens, so the numbers reflect what is connected now rather than
    /// what was connected when the app started.
    /// </summary>
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ViewModel.InitializeAsync();
    }

    /// <summary>Leaving Home stops a copy check that is still reading.</summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        ViewModel.Dispose();
    }

    /// <summary>
    /// Says exactly how many files, in how many folders, and how much would be read, and reads
    /// only if the person presses Compare. Cancel reads nothing.
    /// </summary>
    private async void OnCheckCopiesClick(object sender, RoutedEventArgs e)
    {
        var question = await ViewModel.PrepareCopyCheckAsync();
        if (question is null)
        {
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = question.Title,
            Content = new TextBlock { Text = question.Body, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 },
            PrimaryButtonText = "Compare",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.CompareCopiesAsync(question);
        }
    }
}
