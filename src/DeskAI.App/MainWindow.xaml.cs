using DeskAI.App.Navigation;
using DeskAI.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskAI.App;

public sealed partial class MainWindow : Window
{
    private readonly INavigationService? _navigationService;

    public MainWindow(ShellViewModel viewModel, INavigationService navigationService)
    {
        InitializeComponent();
        Title = "DeskAI";
        RootNavigation.DataContext = viewModel;
        _navigationService = navigationService;
        _navigationService.Initialize(ContentFrame);
        RootNavigation.SelectedItem = RootNavigation.MenuItems[0];
        _navigationService.Navigate("dashboard");
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
        }
    }
}
