namespace DeskAI.Core.Templates;

/// <summary>
/// Checks folder names a person typed before they can become a template.
/// </summary>
/// <remarks>
/// <para>
/// Typed names are the one untrusted input in folder templates. Each name must be a single
/// plain folder name: no path separators, no drive letters, no traversal, none of the characters
/// Windows refuses, no name Windows keeps for devices, no trailing dot. The safety policy
/// checks every name again when the plan is built, so this is the first of two gates, and its
/// job is to say why in words a person can act on.
/// </para>
/// <para>
/// Names are separated by commas or new lines. Blank entries are ignored, so a trailing comma
/// is not a mistake.
/// </para>
/// </remarks>
public static class FolderNameCheck
{
    public const int MaxNameLength = 64;

    private static readonly char[] Separators = [',', '\n', '\r', ';'];

    private static readonly System.Buffers.SearchValues<char> Forbidden =
        System.Buffers.SearchValues.Create(['\\', '/', ':', '*', '?', '"', '<', '>', '|']);

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Reads what was typed into folder names.
    /// </summary>
    /// <returns>The names, in the order typed, or an empty list with <paramref name="problem"/> set.</returns>
    public static IReadOnlyList<string> Parse(string? typed, out string? problem)
    {
        var names = (typed ?? string.Empty)
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
        if (names.Length == 0)
        {
            problem = "Type at least one folder name.";
            return [];
        }

        if (names.Length > FolderTemplateCatalog.MaxFolders)
        {
            problem = $"Up to {FolderTemplateCatalog.MaxFolders} folders at a time.";
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (Check(name) is { } reason)
            {
                problem = reason;
                return [];
            }

            if (!seen.Add(name))
            {
                problem = $"{name} is listed twice.";
                return [];
            }
        }

        problem = null;
        return names;
    }

    /// <summary>Why one name cannot be a folder name, or null when it can.</summary>
    public static string? Check(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length == 0 || name.Trim().Length != name.Length)
        {
            return "A folder name can't be empty or start or end with a space.";
        }

        if (name.Length > MaxNameLength)
        {
            return $"A folder name can be up to {MaxNameLength} letters.";
        }

        if (name.AsSpan().ContainsAny(Forbidden) || name.Any(char.IsControl))
        {
            return $"{name} can't be used. A folder name can't contain \\ / : * ? \" < > or |.";
        }

        if (name.All(letter => letter == '.'))
        {
            return "A folder name can't be only dots.";
        }

        if (name.EndsWith('.'))
        {
            return $"{name} can't end with a dot.";
        }

        var withoutExtension = name.Split('.')[0];
        if (ReservedNames.Contains(name) || ReservedNames.Contains(withoutExtension))
        {
            return $"Windows keeps the name {name} for itself, so it can't be a folder.";
        }

        return null;
    }
}
