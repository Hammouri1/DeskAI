using Windows.Storage.Pickers;

namespace DeskAI.App.Services;

public sealed class WindowsFolderPickerService : IFolderPickerService
{
    public async Task<string?> PickFolderAsync(nint ownerWindowHandle)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, ownerWindowHandle);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
