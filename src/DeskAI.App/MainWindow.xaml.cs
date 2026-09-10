using DeskAI.App.Navigation;
using DeskAI.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskAI.App;

public sealed partial class MainWindow : Window
{
    private readonly INavigationService? _navigationService;
    private readonly ShellViewModel? _shell;

    public MainWindow(ShellViewModel viewModel, INavigationService navigationService)
    {
        InitializeComponent();
        Title = "DeskAI";
        _shell = viewModel;
        RootNavigation.DataContext = viewModel;
        _navigationService = navigationService;
        _navigationService.Initialize(ContentFrame);
        RootNavigation.SelectedItem = RootNavigation.MenuItems[0];
        _navigationService.Navigate("dashboard");
        _ = RefreshScopeAsync();
    }

    /// <summary>
    /// Keeps the pane's scope reminder honest.
    /// </summary>
    /// <remarks>
    /// Refreshed on every navigation because connecting or disconnecting a folder happens
    /// on another page, and a stale reminder about what DeskAI can reach is exactly the
    /// thing this label exists to prevent.
    /// </remarks>
    private async Task RefreshScopeAsync()
    {
        if (_shell is not null)
        {
            await _shell.RefreshAsync();
        }
    }

    /// <summary>
    /// Takes someone to the page that explains the finding. It changes nothing on the way.
    /// </summary>
    private void OnReviewFindingClicked(object sender, RoutedEventArgs args)
    {
        _shell?.DismissFindingCommand.Execute(null);
        _navigationService?.Navigate("automation");
        foreach (var item in RootNavigation.MenuItems)
        {
            if (item is NavigationViewItem { Tag: "automation" } automation)
            {
                RootNavigation.SelectedItem = automation;
                break;
            }
        }

        _ = RefreshScopeAsync();
    }

    private MainWindow()
    {
        InitializeComponent();
        Title = "DeskAI — startup issue";
        RootNavigation.IsEnabled = false;
        ContentFrame.Content = new TextBlock
        {
            Margin = new Thickness(32),
            Text = "DeskAI could not initialize its local application state. No personal folders were accessed. Check the debug log and restart the app.",
            TextWrapping = TextWrapping.Wrap,
        };
    }

    public static MainWindow CreateStartupFailureWindow() => new();

    private void OnNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is string tag)
        {
            _navigationService?.Navigate(tag);
            _ = RefreshScopeAsync();
        }
    }
}
