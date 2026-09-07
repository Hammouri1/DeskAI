using System.Text.RegularExpressions;

namespace DeskAI.Infrastructure.Logging;

public static partial class SensitiveDataRedactor
{
    public static string Redact(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var withoutAuthorization = AuthorizationHeader().Replace(value, "$1[REDACTED]");
        return WindowsAbsolutePath().Replace(withoutAuthorization, "[REDACTED_PATH]");
    }

    [GeneratedRegex("(?i)(authorization\\s*[:=]\\s*(?:bearer\\s+)?)[^\\s,;]+")]
    private static partial Regex AuthorizationHeader();

    [GeneratedRegex(@"(?i)(?:[a-z]:\\|\\\\)[^\r\n\t]+")]
    private static partial Regex WindowsAbsolutePath();
}
