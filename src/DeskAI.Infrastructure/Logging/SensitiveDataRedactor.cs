using System.Text.RegularExpressions;

namespace DeskAI.Infrastructure.Logging;

public static partial class SensitiveDataRedactor
{
    public static string Redact(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var withoutAuthorization = AuthorizationHeader().Replace(value, "$1[REDACTED]");
        var withoutProviderKey = ProviderKeyHeader().Replace(withoutAuthorization, "$1[REDACTED]");
        return WindowsAbsolutePath().Replace(withoutProviderKey, "[REDACTED_PATH]");
    }

    [GeneratedRegex("(?i)(authorization\\s*[:=]\\s*(?:bearer\\s+)?)[^\\s,;]+")]
    private static partial Regex AuthorizationHeader();

    [GeneratedRegex("(?i)((?:x-goog-api-key|api[_-]?key)\\s*[:=]\\s*)[^\\s,;]+")]
    private static partial Regex ProviderKeyHeader();

    [GeneratedRegex(@"(?i)(?:[a-z]:\\|\\\\)[^\r\n\t]+")]
    private static partial Regex WindowsAbsolutePath();
}
