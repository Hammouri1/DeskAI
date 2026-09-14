namespace DeskAI.App.ViewModels;

/// <summary>
/// Which saved search the Search page should run when it next opens, left by My workspace's
/// "Open in Search".
/// </summary>
/// <remarks>
/// It carries a saved-search ID and nothing else — the same thing a person picks from the
/// saved-search list on Search, so it grants nothing. It is taken once, so a later ordinary
/// visit to Search is not steered by an old press.
/// </remarks>
public sealed class SearchRequest
{
    private Guid? _savedSearchId;

    public void Ask(Guid savedSearchId) => _savedSearchId = savedSearchId;

    /// <returns>The requested saved search, which is then forgotten; or null.</returns>
    public Guid? Take()
    {
        var savedSearchId = _savedSearchId;
        _savedSearchId = null;
        return savedSearchId;
    }
}
