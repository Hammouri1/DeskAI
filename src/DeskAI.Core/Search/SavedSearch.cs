namespace DeskAI.Core.Search;

/// <summary>
/// A saved search. Running one finds files; it never moves, copies, or changes any.
/// </summary>
/// <remarks>
/// <para>
/// A collection stores the phrase that was typed, not the query it produced. That keeps a
/// relative phrase relative: "photos from last month" means the month before you run it,
/// not the month it was saved. The cost is that changing the translator's vocabulary can
/// change what an old collection matches, which is the right trade for a saved search.
/// </para>
/// <para>
/// It deliberately stores no root IDs. Scope is re-read from the authorized folders every
/// time it runs, so disconnecting a folder immediately narrows every collection, and a
/// saved collection can never act as a lingering grant to somewhere revoked.
/// </para>
/// <para>
/// A collection is virtual. Nothing about it can become a file operation.
/// </para>
/// </remarks>
public sealed record SavedSearch
{
    public const int MaxNameLength = 60;

    /// <summary>Keeps the list readable and bounds what one person can accumulate.</summary>
    public const int MaxSavedSearches = 50;

    /// <summary>
    /// How many saved searches may be pinned to My workspace at once.
    /// </summary>
    /// <remarks>
    /// Pins are for the few searches someone reaches for daily. Past a handful the page stops
    /// being glanceable and becomes a second copy of the saved-search list.
    /// </remarks>
    public const int MaxPinned = 8;

    private SavedSearch(Guid id, string name, string phrase, DateTimeOffset createdAtUtc, bool isPinned)
    {
        Id = id;
        Name = name;
        Phrase = phrase;
        CreatedAtUtc = createdAtUtc;
        IsPinned = isPinned;
    }

    public Guid Id { get; }

    public string Name { get; }

    /// <summary>The phrase as typed, re-read on every run.</summary>
    public string Phrase { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>Whether this search is shown as a tile on My workspace.</summary>
    public bool IsPinned { get; }

    public SavedSearch WithPinned(bool isPinned) => new(Id, Name, Phrase, CreatedAtUtc, isPinned);

    public static SavedSearch Create(
        Guid id,
        string name,
        string phrase,
        DateTimeOffset createdAtUtc,
        bool isPinned = false)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A saved search needs a stable ID.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(phrase);

        var trimmedName = name.Trim();
        if (trimmedName.Length > MaxNameLength)
        {
            throw new ArgumentException(
                $"A saved search name cannot be longer than {MaxNameLength} characters.",
                nameof(name));
        }

        var trimmedPhrase = phrase.Trim();
        return trimmedPhrase.Length > NaturalLanguageQueryTranslator.MaxInputLength
            ? throw new ArgumentException(
                $"A saved search phrase cannot be longer than {NaturalLanguageQueryTranslator.MaxInputLength} characters.",
                nameof(phrase))
            : new SavedSearch(id, trimmedName, trimmedPhrase, createdAtUtc, isPinned);
    }
}
