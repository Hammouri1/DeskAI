namespace DeskAI.App.Services;

/// <summary>How choosing a folder ended.</summary>
public enum FolderPickOutcome
{
    /// <summary>A folder was chosen and DeskAI can name it.</summary>
    Picked,

    /// <summary>The person closed the dialog without choosing.</summary>
    Cancelled,

    /// <summary>
    /// Something was chosen, but Windows gave DeskAI no usable location for it.
    /// </summary>
    /// <remarks>
    /// This happens for places that are not really folders on a disk — a phone, a camera, or
    /// some cloud locations that appear in the dialog but have no path behind them.
    /// </remarks>
    Unavailable,
}

/// <summary>The chosen folder, or the reason there isn't one.</summary>
/// <remarks>
/// Cancelling and failing used to be the same answer — an empty string — so a folder DeskAI
/// could not use looked exactly like a person changing their mind, and the app said nothing
/// at all. Pressing a button and getting silence is its own kind of wrong answer, so the two
/// are separate cases now.
/// </remarks>
public sealed record FolderPickResult(FolderPickOutcome Outcome, string? Path)
{
    public static FolderPickResult Cancelled { get; } = new(FolderPickOutcome.Cancelled, null);

    public static FolderPickResult Unavailable { get; } = new(FolderPickOutcome.Unavailable, null);

    public static FolderPickResult Picked(string path) => new(FolderPickOutcome.Picked, path);

    public bool WasPicked => Outcome == FolderPickOutcome.Picked && !string.IsNullOrWhiteSpace(Path);
}

public interface IFolderPickerService
{
    Task<FolderPickResult> PickFolderAsync(nint ownerWindowHandle);
}
