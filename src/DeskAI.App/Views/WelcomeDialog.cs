using DeskAI.App.ViewModels;
using DeskAI.Core.Roots;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;

namespace DeskAI.App.Views;

/// <summary>
/// The first-run welcome pop-up. Next and Back stay inside it; Skip, Esc, and Done close
/// it. A Connect button only records the folder and closes it; the window then asks Home's question.
/// </summary>
internal static class WelcomeDialog
{
    /// <returns>True when a Connect button was pressed; <see cref="WelcomeViewModel.ChosenFolder"/> names it.</returns>
    public static async Task<bool> ShowAsync(XamlRoot xamlRoot, WelcomeViewModel welcome)
    {
        var chose = false;
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            CloseButtonText = "Skip",
            SecondaryButtonText = "Back",
            DefaultButton = ContentDialogButton.Primary,
        };

        void Render()
        {
            dialog.Title = welcome.Current.Title;
            dialog.PrimaryButtonText = welcome.NextText;
            dialog.IsSecondaryButtonEnabled = welcome.CanGoBack;
            dialog.Content = BuildPage(welcome, kind =>
            {
                welcome.Choose(kind);
                chose = true;
                dialog.Hide();
            });
        }

        // Cancelling the click keeps the pop-up open while the page changes under it.
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!welcome.Next())
            {
                args.Cancel = true;
                Render();
            }
        };
        dialog.SecondaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            welcome.Back();
            Render();
        };

        Render();
        await dialog.ShowAsync();
        return chose;
    }

    private static StackPanel BuildPage(WelcomeViewModel welcome, Action<PersonalFolderKind> connect)
    {
        var page = new StackPanel { Spacing = 14, MaxWidth = 440, MinWidth = 360 };
        page.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("ms-appx:///Assets/DeskAI.Logo.png")),
            Width = 56,
            Height = 56,
            HorizontalAlignment = HorizontalAlignment.Left,
        });

        var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        AutomationProperties.SetName(dots, welcome.PageNumberText);
        foreach (var isCurrent in welcome.Dots)
        {
            dots.Children.Add(new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                Opacity = isCurrent ? 1 : 0.3,
            });
        }

        page.Children.Add(dots);
        if (welcome.Current.ShowsBuddy)
        {
            // Sparky, the quick search buddy (ADR 0047). Decorative: the body line says what matters.
            page.Children.Add(new Buddies.SparkyBuddy
            {
                Width = 96,
                Height = 96,
                HorizontalAlignment = HorizontalAlignment.Left,
            });
        }

        if (welcome.Current.Body.Length > 0)
        {
            page.Children.Add(new TextBlock { Text = welcome.Current.Body, TextWrapping = TextWrapping.Wrap });
        }

        foreach (var promise in welcome.Current.Promises)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(new FontIcon { Glyph = "", FontSize = 14 });
            row.Children.Add(new TextBlock { Text = promise, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 });
            page.Children.Add(row);
        }

        if (welcome.IsLastPage)
        {
            if (welcome.Folders.HasNoRows)
            {
                page.Children.Add(new TextBlock { Text = PersonalFoldersViewModel.NoneKnownText, TextWrapping = TextWrapping.Wrap });
            }

            foreach (var folder in welcome.Folders.Rows)
            {
                var button = new Button { Content = folder.ButtonName, HorizontalAlignment = HorizontalAlignment.Stretch };
                button.Click += (_, _) => connect(folder.Kind);
                page.Children.Add(button);
            }

            page.Children.Add(new TextBlock
            {
                Text = welcome.AiLine,
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["CaptionStyle"],
            });
        }

        return page;
    }
}
