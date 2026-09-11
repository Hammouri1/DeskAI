namespace DeskAI.Core.Content;

/// <summary>Whether a file was read, or why it was not.</summary>
public enum FingerprintStatus
{
    Read,
    NotAllowed,
    Blocked,
    Link,
    OnlineOnly,
    Changed,
    Busy,
    Missing,
    Unavailable,
}

/// <summary>
/// A SHA-256 fingerprint of a file's bytes (all of them, or its beginning), or the reason there
/// is none, in words written for the person.
/// </summary>
/// <remarks>
/// Two files of the same size with the same whole-file fingerprint are identical for every
/// practical purpose. The fingerprint is held in memory for one check and kept nowhere.
/// </remarks>
public sealed record FileFingerprint(FingerprintStatus Status, string? Hash, long BytesRead, string Explanation)
{
    public bool WasRead => Status == FingerprintStatus.Read;

    public static FileFingerprint Refused(FingerprintStatus status, string explanation) => new(status, null, 0, explanation);
}
