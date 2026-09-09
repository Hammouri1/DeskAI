namespace DeskAI.Core.Roots;

public sealed record AuthorizedRoot
{
    private AuthorizedRoot(
        Guid id,
        string canonicalPath,
        string displayName,
        RootAccessLevel permission,
        RootAuthorizationScope authorizationScope)
    {
        Id = id;
        CanonicalPath = canonicalPath;
        DisplayName = displayName;
        Permission = permission;
        AuthorizationScope = authorizationScope;
    }

    public Guid Id { get; }

    public string CanonicalPath { get; }

    public string DisplayName { get; }

    public RootAccessLevel Permission { get; }
    public RootAuthorizationScope AuthorizationScope { get; }

    public static AuthorizedRoot Create(
        Guid id,
        string canonicalPath,
        string displayName,
        RootAccessLevel permission,
        RootAuthorizationScope authorizationScope = RootAuthorizationScope.Organize)
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

        return new AuthorizedRoot(id, canonicalPath, displayName, permission, authorizationScope);
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
}

public enum RootAccessLevel
{
    Allowed,
    Restricted,
    Protected,
}
