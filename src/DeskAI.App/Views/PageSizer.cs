using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskAI.App.Views;

/// <summary>Gives scrollable pages the viewport width while keeping a readable maximum.</summary>
/// <remarks>
/// A vertical ScrollViewer measures its content by the content's preferred width. A StackPanel
/// then shrinks to its longest sentence, leaving most of a wide window empty. This sets the
/// width from the actual page viewport, minus the two 32-pixel page margins, and updates it
/// when the person resizes the window. It changes layout only, never a page's behavior.
/// </remarks>
internal static class PageSizer
{
    private const double HorizontalMargins = 64;
    private const double MaximumContentWidth = 1320;

    internal static void Attach(Page page, FrameworkElement content)
    {
        void Update() => content.Width = Math.Max(0, Math.Min(
            MaximumContentWidth, page.ActualWidth - HorizontalMargins));

        page.Loaded += (_, _) => Update();
        page.SizeChanged += (_, _) => Update();
    }
}
