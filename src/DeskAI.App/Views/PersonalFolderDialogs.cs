using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskAI.App.Views;

/// <summary>The one dialog behind every Connect button on the "Your folders" card, on Home and My workspace.</summary>
internal static class PersonalFolderDialogs
{
    /// <returns>True only if the person pressed the connect button; Enter, Esc, and the X all cancel.</returns>
    public static async Task<bool> ConfirmConnectAsync(XamlRoot xamlRoot, string folderName)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = $"Connect your {folderName}?",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 480,
                Text = $"DeskAI will remember the names, sizes, and dates of the files in your {folderName}. It reads nothing inside them and moves nothing.\n\n"
                    + "Organize then asks your permission and shows what it would move before anything moves. Shortcuts are left alone.",
            },
            PrimaryButtonText = $"Connect my {folderName}",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await confirm.ShowAsync() == ContentDialogResult.Primary;
    }
}
