using System.Text;

namespace DeskAI.FolderColorProbe;

/// <summary>Just enough <c>desktop.ini</c> reading for the probe's checks.</summary>
internal static class DesktopIni
{
    private static readonly string[] IconKeys = ["IconResource", "IconFile", "IconIndex"];

    /// <summary>
    /// desktop.ini is UTF-16 or UTF-8 when it starts with a byte-order mark, otherwise the ANSI
    /// code page; the probe's own paths are plain ASCII either way.
    /// </summary>
    internal static string Text(byte[] bytes) =>
        bytes switch
        {
            [0xFF, 0xFE, ..] => Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2),
            [0xEF, 0xBB, 0xBF, ..] => Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3),
            _ => Encoding.Latin1.GetString(bytes),
        };

    internal static string? Value(string text, string section, string key)
    {
        var inSection = false;
        foreach (var line in Lines(text))
        {
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inSection = string.Equals(line[1..^1].Trim(), section, StringComparison.OrdinalIgnoreCase);
            }
            else if (inSection && KeyOf(line) is { } name && string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
            {
                return line[(line.IndexOf('=', StringComparison.Ordinal) + 1)..].Trim();
            }
        }

        return null;
    }

    /// <summary>Every non-blank line except the icon keys that colouring is allowed to change.</summary>
    internal static IReadOnlyList<string> LinesWithoutIcon(string text) =>
        Lines(text).Where(line => KeyOf(line) is not { } key || !IconKeys.Contains(key, StringComparer.OrdinalIgnoreCase)).ToList();

    private static IEnumerable<string> Lines(string text) =>
        text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0);

    private static string? KeyOf(string line)
    {
        var equals = line.IndexOf('=', StringComparison.Ordinal);
        return equals > 0 && !line.StartsWith('[') ? line[..equals].Trim() : null;
    }
}
