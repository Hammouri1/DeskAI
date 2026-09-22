using Windows.Storage.Pickers;

namespace DeskAI.App.Services;

public sealed class WindowsFolderPickerService : IFolderPickerService
{
    public async Task<FolderPickResult> PickFolderAsync(nint ownerWindowHandle)
    {
        var picker = new FolderPicker
        {
            // DeskAI may connect only a personal folder or something inside one. Starting at
            // Documents makes the first visible choice valid instead of inviting a whole drive
            // or program folder that deterministic policy must refuse.
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, ownerWindowHandle);

        Windows.Storage.StorageFolder? folder;
        try
        {
            folder = await picker.PickSingleFolderAsync();
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            // The dialog itself failed. Report it rather than letting it look like the
            // person simply changed their mind.
            return FolderPickResult.Unavailable;
        }

        if (folder is null)
        {
            return FolderPickResult.Cancelled;
        }

        // Some things the dialog will happily let you select are not folders on a disk — a
        // phone, a camera, certain cloud locations — and Windows hands back no path for
        // them. DeskAI cannot work with those, and must say so instead of doing nothing.
        return string.IsNullOrWhiteSpace(folder.Path)
            ? FolderPickResult.Unavailable
            : FolderPickResult.Picked(folder.Path);
    }
}
