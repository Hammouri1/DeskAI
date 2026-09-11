using DeskAI.App.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace DeskAI.App.Navigation;

public sealed class NavigationService(IServiceProvider serviceProvider) : INavigationService
{
    private static readonly Dictionary<string, Type> Routes = new(StringComparer.Ordinal)
    {
        ["dashboard"] = typeof(DashboardPage),
        ["organize"] = typeof(OrganizePage),
        ["practice"] = typeof(PracticePage),
        ["search"] = typeof(SearchPage),
        ["automation"] = typeof(AutomationPage),
        ["settings"] = typeof(SettingsPage),
    };

    private Frame? _frame;

    public void Initialize(Frame frame) => _frame = frame ?? throw new ArgumentNullException(nameof(frame));

    public bool Navigate(string route, bool fresh = false)
    {
        if (_frame is null || !Routes.TryGetValue(route, out var pageType))
        {
            return false;
        }

        if (!fresh && _frame.Content?.GetType() == pageType)
        {
            return true;
        }

        _frame.Content = serviceProvider.GetRequiredService(pageType);
        return true;
    }
}
