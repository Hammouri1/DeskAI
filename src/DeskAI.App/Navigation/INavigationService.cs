using Microsoft.UI.Xaml.Controls;

namespace DeskAI.App.Navigation;

public interface INavigationService
{
    void Initialize(Frame frame);

    bool Navigate(string route);
}
