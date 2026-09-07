namespace DeskAI.Core.Roots;

public sealed record AuthorizedRoot
{
    private AuthorizedRoot(Guid id, string canonicalPath, string displayName, RootAccessLevel permission)
    {
        Id = id;
        CanonicalPath = canonicalPath;
        DisplayName = displayName;
        Permission = permission;
    }

    public Guid Id { get; }

    public string CanonicalPath { get; }

    public string DisplayName { get; }

    public RootAccessLevel Permission { get; }

    public static AuthorizedRoot Create(
        Guid id,
        string canonicalPath,
        string displayName,
        RootAccessLevel permission)
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

        return new AuthorizedRoot(id, canonicalPath, displayName, permission);
    }
}

public enum RootAccessLevel
{
    Allowed,
    Restricted,
    Protected,
}
