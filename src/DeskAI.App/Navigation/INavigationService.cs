using Microsoft.UI.Xaml.Controls;

namespace DeskAI.App.Navigation;

public interface INavigationService
{
    void Initialize(Frame frame);

    /// <param name="fresh">
    /// Open a new copy of the page even if it is already showing, so it starts again — used
    /// when something has asked the page to open on a particular folder.
    /// </param>
    bool Navigate(string route, bool fresh = false);
}
