using Windows.Storage.Pickers;

namespace DeskAI.App.Services;

/// <summary>Lets the person choose where a backup file goes, or which one to restore. Lists nothing itself.</summary>
public interface IBackupFilePickerService
{
    Task<FolderPickResult> PickSaveAsync(nint ownerWindowHandle, string suggestedFileName);

    Task<FolderPickResult> PickOpenAsync(nint ownerWindowHandle);
}

/// <summary>
/// The Windows save and open dialogs, limited to <c>.json</c> files.
/// </summary>
/// <remarks>
/// The same three outcomes as the folder picker: picked, cancelled, or a choice Windows gave no
/// path for. Only the path is returned; the file itself is written or read by
/// <c>UserFileStore</c>, which checks the path again.
/// </remarks>
public sealed class WindowsBackupFilePickerService : IBackupFilePickerService
{
    public async Task<FolderPickResult> PickSaveAsync(nint ownerWindowHandle, string suggestedFileName)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedFileName,
        };
        picker.FileTypeChoices.Add("DeskAI backup", [".json"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, ownerWindowHandle);

        Windows.Storage.StorageFile? file;
        try
        {
            file = await picker.PickSaveFileAsync();
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            return FolderPickResult.Unavailable;
        }

        return Describe(file);
    }

    public async Task<FolderPickResult> PickOpenAsync(nint ownerWindowHandle)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(".json");
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

        return Describe(file);
    }

    private static FolderPickResult Describe(Windows.Storage.StorageFile? file)
    {
        if (file is null)
        {
            return FolderPickResult.Cancelled;
        }

        return string.IsNullOrWhiteSpace(file.Path)
            ? FolderPickResult.Unavailable
            : FolderPickResult.Picked(file.Path);
    }
}
