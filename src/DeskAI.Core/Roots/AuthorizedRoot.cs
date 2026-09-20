namespace DeskAI.Core.Roots;

public sealed record AuthorizedRoot
{
    private AuthorizedRoot(
        Guid id,
        string canonicalPath,
        string displayName,
        RootAccessLevel permission,
        RootAuthorizationScope authorizationScope,
        DateTimeOffset? tidyAllowedSinceUtc)
    {
        Id = id;
        CanonicalPath = canonicalPath;
        DisplayName = displayName;
        Permission = permission;
        AuthorizationScope = authorizationScope;
        TidyAllowedSinceUtc = tidyAllowedSinceUtc;
    }

    public Guid Id { get; }

    public string CanonicalPath { get; }

    public string DisplayName { get; }

    public RootAccessLevel Permission { get; }
    public RootAuthorizationScope AuthorizationScope { get; }

    /// <summary>
    /// When the person allowed DeskAI to tidy this folder, or null if they have not.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="AuthorizationScope"/>, which says what may be <em>read</em>.
    /// Tidying is a separate yes, stored in its own table, so switching reading inside files on
    /// or off can neither drop nor grant it. Ask <see cref="RootCapabilities.CanTidy"/>.
    /// </remarks>
    public DateTimeOffset? TidyAllowedSinceUtc { get; }

    public AuthorizedRoot WithTidyAllowedSince(DateTimeOffset? sinceUtc) =>
        new(Id, CanonicalPath, DisplayName, Permission, AuthorizationScope, sinceUtc);

    /// <remarks>
    /// There is deliberately no default scope. A default is what a caller gets by forgetting,
    /// and the old default was the scope that may be changed.
    /// </remarks>
    public static AuthorizedRoot Create(
        Guid id,
        string canonicalPath,
        string displayName,
        RootAccessLevel permission,
        RootAuthorizationScope authorizationScope)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An authorized root needs a stable ID.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (!Path.IsPathFullyQualified(canonicalPath))
        {
            throw new ArgumentException("An authorized root must be an absolute path.", nameof(canonicalPath));
        }

        return new AuthorizedRoot(id, canonicalPath, displayName, permission, authorizationScope, null);
    }
}

/// <summary>
/// Why a folder was connected, and therefore what DeskAI may do with it.
/// </summary>
/// <remarks>
/// <para>
/// These are stored in the database as their numeric values, so a value must only ever be
/// <em>appended</em>. Inserting or reordering one would silently re-label every folder a
/// person has already connected — a folder authorized for metadata could come back as a
/// folder authorized to be changed.
/// </para>
/// <para>
/// Ask <see cref="RootCapabilities"/> what a scope permits rather than comparing to a value
/// directly, so that adding a scope cannot quietly grant it rights nobody meant to give.
/// </para>
/// </remarks>
public enum RootAuthorizationScope
{
    /// <summary>Names, sizes, and dates may be read. Nothing may be opened or changed.</summary>
    MetadataOnly = 0,

    /// <summary>The generated practice workspace, which may be changed.</summary>
    ControlledDemo = 1,

    /// <summary>A folder connected for organizing, which may be changed after approval.</summary>
    Organize = 2,

    /// <summary>
    /// Metadata, plus permission to open files and read what is inside them. Still grants
    /// no permission to move, rename, or delete anything.
    /// </summary>
    MetadataAndContent = 3,

    /// <summary>Separately approved reading of plain text and bounded modern Office documents.</summary>
    MetadataAndDocuments = 4,

    /// <summary>Separately approved local PDF text reading in addition to notes and Office files.</summary>
    MetadataDocumentsAndPdf = 5,

    /// <summary>Separately approved slide text reading without PDF permission.</summary>
    MetadataDocumentsAndSlides = 6,

    /// <summary>Separate PDF and slide grants are both active.</summary>
    MetadataDocumentsPdfAndSlides = 7,
}

public enum RootAccessLevel
{
    Allowed,
    Restricted,
    Protected,
}
