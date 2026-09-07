namespace DeskAI.App.Services;

public interface IFolderPickerService
{
    Task<string?> PickFolderAsync(nint ownerWindowHandle);
}
