namespace DeskAI.App.ViewModels;

/// <summary>
/// Which folder the Organize page should show when it next opens, left by another part of the
/// app — today only "Review in Organize" on an automatic check's notice.
/// </summary>
/// <remarks>
/// It carries a folder ID and nothing else. Opening a folder in the page is exactly what a
/// person could do from the folder list, so it grants nothing: tidying still needs that
/// folder's permission and a press of Tidy. It is taken once, so a later ordinary visit to
/// Organize is not steered by an old notice.
/// </remarks>
public sealed class OrganizeRequest
{
    private Guid? _folderId;

    public void Ask(Guid folderId) => _folderId = folderId;

    /// <returns>The requested folder, which is then forgotten; or null.</returns>
    public Guid? Take()
    {
        var folderId = _folderId;
        _folderId = null;
        return folderId;
    }
}
