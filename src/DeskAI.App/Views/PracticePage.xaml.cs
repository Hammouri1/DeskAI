using DeskAI.App.Navigation;
using DeskAI.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskAI.App.Views;

/// <summary>
/// Practice mode: the tidy-up flow on files DeskAI makes up in a temporary folder.
/// </summary>
/// <remarks>
/// Reached from the "Try it on example files first" link on the Organize page, for people
/// who want to see tidying and undo work before letting DeskAI near a folder of their own.
/// </remarks>
public sealed partial class PracticePage : Page
{
    private readonly PracticeViewModel _viewModel;
    private readonly INavigationService _navigation;

    public PracticePage(PracticeViewModel viewModel, INavigationService navigation)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _navigation = navigation;
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnUnloaded;
        _viewModel.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => _navigation.Navigate("organize");
}
