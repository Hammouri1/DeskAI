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

public enum RootAuthorizationScope
{
    MetadataOnly,
    ControlledDemo,
    Organize,
}

public enum RootAccessLevel
{
    Allowed,
    Restricted,
    Protected,
}
