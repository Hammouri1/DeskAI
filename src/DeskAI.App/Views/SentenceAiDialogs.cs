using DeskAI.Core.Ai;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskAI.App.Views;

/// <summary>
/// The one dialog behind "Let AI read this" on Search and Automatic tasks: it shows the exact
/// sentence, who gets it, and where, and sends only if the person presses Send.
/// </summary>
internal static class SentenceAiDialogs
{
    public static async Task<bool> ConfirmSendAsync(XamlRoot xamlRoot, SentenceAiQuestion question)
    {
        var content = new StackPanel { Spacing = 12, MaxWidth = 480 };
        content.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = $"{question.ServiceName}, at {question.Destination}, will see exactly this, and nothing else:",
        });
        content.Children.Add(new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(4),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DeskSurfaceBrush"],
            Child = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = question.Sentence, IsTextSelectionEnabled = true },
        });
        content.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = "Nothing about your files is sent: no names, folders, or locations. The AI answers with a reading DeskAI "
                + "puts in the box for you to check. It cannot search, move, or change anything itself.",
        });

        var confirm = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = $"Send this to {question.ServiceName}?",
            Content = content,
            PrimaryButtonText = "Send",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await confirm.ShowAsync() == ContentDialogResult.Primary;
    }
}
