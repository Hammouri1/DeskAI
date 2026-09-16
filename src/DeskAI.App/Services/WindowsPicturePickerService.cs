using Windows.Storage.Pickers;

namespace DeskAI.App.Services;

/// <summary>Lets the person pick one picture file in the Windows dialog. Lists nothing itself.</summary>
public interface IPicturePickerService
{
    Task<FolderPickResult> PickPictureAsync(nint ownerWindowHandle);
}

/// <summary>
/// The Windows file dialog, limited to picture files.
/// </summary>
/// <remarks>
/// The same three outcomes as the folder picker: picked, cancelled, or a choice Windows gave no
/// path for. The path goes straight to the wallpaper service, which checks it again; DeskAI never
/// opens the file.
/// </remarks>
public sealed class WindowsPicturePickerService : IPicturePickerService
{
    public async Task<FolderPickResult> PickPictureAsync(nint ownerWindowHandle)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            ViewMode = PickerViewMode.Thumbnail,
        };
        foreach (var extension in new[] { ".jpg", ".jpeg", ".png", ".bmp" })
        {
            picker.FileTypeFilter.Add(extension);
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, ownerWindowHandle);

        Windows.Storage.StorageFile? file;
        try
        {
            file = await picker.PickSingleFileAsync();
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            return FolderPickResult.Unavailable;
        }

        if (file is null)
        {
            return FolderPickResult.Cancelled;
        }

        return string.IsNullOrWhiteSpace(file.Path)
            ? FolderPickResult.Unavailable
            : FolderPickResult.Picked(file.Path);
    }
}
