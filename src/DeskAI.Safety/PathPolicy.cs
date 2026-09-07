using DeskAI.Core.Plans;
using DeskAI.Core.Roots;

namespace DeskAI.Safety;

public interface IPathPolicy
{
    ValidationResult ValidateRelativePath(AuthorizedRoot root, string relativePath);
}

public sealed class WindowsPathPolicy : IPathPolicy
{
    private static readonly char[] UnsupportedWildcards = ['*', '?'];
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private readonly IReadOnlyList<string> _permanentlyProtectedRoots;
    private readonly IReadOnlyList<string> _userProtectedEntries;

    public WindowsPathPolicy(
        IEnumerable<string>? permanentlyProtectedRoots = null,
        IEnumerable<string>? userProtectedEntries = null)
    {
        _permanentlyProtectedRoots = CanonicalizeConfiguredPaths(permanentlyProtectedRoots);
        _userProtectedEntries = CanonicalizeConfiguredPaths(userProtectedEntries);
    }

    public ValidationResult ValidateRelativePath(AuthorizedRoot root, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (root.Permission == RootAccessLevel.Protected)
        {
            return ValidationResult.Blocked(
                ValidationReasonCode.ProtectedRoot,
                "The selected root is protected and cannot be changed.");
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return ValidationResult.Blocked(ValidationReasonCode.EmptyPath, "The path is empty.");
        }

        if (Path.IsPathFullyQualified(relativePath) || Path.IsPathRooted(relativePath))
        {
            return ValidationResult.Blocked(
                ValidationReasonCode.RelativePath,
                "Operations must use paths relative to the authorized root.");
        }

        var rawSegments = relativePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (rawSegments.Any(segment => segment.Equals("..", StringComparison.Ordinal)))
        {
            return ValidationResult.Blocked(
                ValidationReasonCode.PathTraversal,
                "The path contains a parent-directory traversal segment.");
        }

        if (IsUnsupported(relativePath))
        {
            return ValidationResult.Blocked(
                ValidationReasonCode.UnsupportedPath,
                "The path uses an unsupported Windows path form or name.");
        }

        string canonicalRoot;
        string candidate;
        try
        {
            canonicalRoot = Normalize(root.CanonicalPath);
            candidate = Path.GetFullPath(relativePath, canonicalRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return ValidationResult.Blocked(ValidationReasonCode.UnsupportedPath, "The path is malformed or unsupported.");
        }

        if (!IsContainedBy(canonicalRoot, candidate))
        {
            return ValidationResult.Blocked(
                ValidationReasonCode.OutsideAuthorizedRoot,
                "The path resolves outside the authorized root.");
        }

        if (_permanentlyProtectedRoots.Any(path => PathsOverlap(path, candidate)))
        {
            return ValidationResult.Blocked(
                ValidationReasonCode.ProtectedRoot,
                "The path overlaps a permanently protected location.");
        }

        if (_userProtectedEntries.Any(path => PathsOverlap(path, candidate)))
        {
            return ValidationResult.Blocked(
                ValidationReasonCode.ProtectedEntry,
                "The path overlaps a user-protected entry.");
        }

        return ValidationResult.Allowed();
    }

    private static string[] CanonicalizeConfiguredPaths(IEnumerable<string>? paths) =>
        (paths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path))
            .Select(Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsUnsupported(string relativePath)
    {
        if (relativePath.StartsWith('\\') ||
            relativePath.StartsWith('/') ||
            relativePath.Contains(':') ||
            relativePath.IndexOfAny(UnsupportedWildcards) >= 0)
        {
            return true;
        }

        var segments = relativePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 || segments.Any(segment =>
            segment.EndsWith(' ') ||
            segment.EndsWith('.') ||
            ReservedNames.Contains(Path.GetFileNameWithoutExtension(segment)));
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsContainedBy(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return relative == "." ||
            (!relative.Equals("..", StringComparison.Ordinal) &&
             !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
             !Path.IsPathRooted(relative));
    }

    private static bool PathsOverlap(string protectedPath, string candidate) =>
        IsContainedBy(protectedPath, candidate) || IsContainedBy(candidate, protectedPath);
}
