using System.Text.Json;

namespace DeskAI.AI;

/// <summary>
/// The service's own one-line reason for refusing, made safe to show.
/// </summary>
/// <remarks>
/// "The key was not accepted" has several causes a person can fix — a mistyped key, a deleted
/// one, one belonging to another service — and the service's own words usually say which. That
/// text is untrusted: it is shown as text only, stripped of control characters, shortened, and
/// the saved key is removed from it in case the service echoes it back.
/// </remarks>
internal static class ServiceReply
{
    private const int MaximumExplanationLength = 200;

    public static string? Explanation(string body, string key)
    {
        string? message;
        try
        {
            using var document = JsonDocument.Parse(body);
            message = document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.Object &&
                error.TryGetProperty("message", out var text) &&
                text.ValueKind == JsonValueKind.String
                    ? text.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var cleaned = new string(message.Select(character => char.IsControl(character) ? ' ' : character).ToArray())
            .Replace(key, "[your key]", StringComparison.Ordinal)
            .Trim();
        return cleaned.Length <= MaximumExplanationLength
            ? cleaned
            : string.Concat(cleaned.AsSpan(0, MaximumExplanationLength), "…");
    }
}
