namespace DeskAI.App.ViewModels;

/// <summary>
/// What the Search page should run when it next opens: a saved search, left by My workspace's
/// "Open in Search", or a typed phrase, left by the search box in the top bar.
/// </summary>
/// <remarks>
/// It carries a saved-search ID or a phrase and nothing else — the same things a person picks
/// or types on Search, so it grants nothing. It is taken once, so a later ordinary visit to
/// Search is not steered by an old press. Asking for one forgets the other.
/// </remarks>
public sealed class SearchRequest
{
    private Guid? _savedSearchId;
    private string? _phrase;

    public void Ask(Guid savedSearchId)
    {
        _savedSearchId = savedSearchId;
        _phrase = null;
    }

    public void AskPhrase(string phrase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phrase);
        _phrase = phrase;
        _savedSearchId = null;
    }

    /// <returns>The requested saved search, which is then forgotten; or null.</returns>
    public Guid? Take()
    {
        var savedSearchId = _savedSearchId;
        _savedSearchId = null;
        return savedSearchId;
    }

    /// <returns>The requested phrase, which is then forgotten; or null.</returns>
    public string? TakePhrase()
    {
        var phrase = _phrase;
        _phrase = null;
        return phrase;
    }
}
