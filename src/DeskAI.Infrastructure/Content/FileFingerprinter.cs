using System.Security.Cryptography;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Content;
using DeskAI.Core.Execution;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;
using DeskAI.Safety;

namespace DeskAI.Infrastructure.Content;

/// <summary>
/// Reads a file in a connected folder and returns a SHA-256 fingerprint of its bytes.
/// </summary>
/// <remarks>
/// <para>
/// One of the two places DeskAI opens a file (the other is <see cref="PlainTextExtractor"/>).
/// Every check comes before the file is opened, the first refusal wins: the folder must be
/// connected and usable; the path must not be protected and must stay inside the folder; the
/// file must exist, not be a link, not be online-only (reading would download it), and still have
/// the size and last-changed time DeskAI remembered.
/// </para>
/// <para>
/// It opens read-only and lets other programs only read meanwhile, so a file being written is
/// reported as busy instead of being fingerprinted while it changes. The link check is repeated
/// once the handle is open, and the size and time once reading is done. Nothing is written,
/// created, or kept: the fingerprint goes back to the caller and nowhere else.
/// </para>
/// </remarks>
public sealed class FileFingerprinter(IPathPolicy pathPolicy) : IFileFingerprinter
{
    // Not named in the FileAttributes enum, but set by Windows on cloud placeholders.
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
    private const int SharingViolation = 32;
    private const int LockViolation = 33;
    private const int BufferSize = 81920;

    public async Task<FileFingerprint> FingerprintAsync(
        AuthorizedRoot root,
        string relativePath,
        ExpectedFile expected,
        long? maxBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(expected);
        cancellationToken.ThrowIfCancellationRequested();

        // Permission first, before any path work.
        if (!RootCapabilities.CanReadMetadata(root))
        {
            return FileFingerprint.Refused(FingerprintStatus.NotAllowed, "That folder is not connected, so DeskAI did not open it.");
        }

        if (pathPolicy.ValidateRoot(root).Status == ValidationStatus.Blocked ||
            pathPolicy.ValidateRelativePath(root, relativePath).Status == ValidationStatus.Blocked)
        {
            return FileFingerprint.Refused(FingerprintStatus.Blocked, "This location is protected, so DeskAI did not open it.");
        }

        var full = ResolveInsideRoot(root.CanonicalPath, relativePath);
        if (full is null)
        {
            return FileFingerprint.Refused(FingerprintStatus.Blocked, "That path leads outside the connected folder.");
        }

        try
        {
            // Any folder on the way, the connected folder included, may have become a link
            // since DeskAI last looked; the file's own check below would not see that.
            if (CrossesLink(root.CanonicalPath, full))
            {
                return FileFingerprint.Refused(FingerprintStatus.Link, "Part of the way there is a shortcut to somewhere else, so DeskAI did not follow it.");
            }

            if (Describe(full, expected) is { } refusal)
            {
                return refusal;
            }

            await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);

            // A file can be replaced by a link between the check above and the open.
            if (IsLink(new FileInfo(full)))
            {
                return FileFingerprint.Refused(FingerprintStatus.Link, "It changed into a shortcut while DeskAI was opening it, so it was not read.");
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[BufferSize];
            var limit = maxBytes ?? long.MaxValue;
            long total = 0;
            while (total < limit)
            {
                var wanted = (int)Math.Min(buffer.Length, limit - total);
                var read = await stream.ReadAsync(buffer.AsMemory(0, wanted), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, read);
                total += read;
            }

            // Changed while it was being read: the fingerprint would describe no real moment.
            if (Describe(full, expected) is not null)
            {
                return new FileFingerprint(FingerprintStatus.Changed, null, total, "It changed while DeskAI was reading it, so it was not compared.");
            }

            return new FileFingerprint(FingerprintStatus.Read, Convert.ToHexString(hash.GetHashAndReset()), total, "Read.");
        }
        catch (IOException exception) when ((exception.HResult & 0xFFFF) is SharingViolation or LockViolation)
        {
            return FileFingerprint.Refused(FingerprintStatus.Busy, "It's open in another program, so it was not read.");
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return FileFingerprint.Refused(FingerprintStatus.Missing, "It is no longer there.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return FileFingerprint.Refused(FingerprintStatus.Unavailable, "Windows did not let DeskAI read it.");
        }
    }

    /// <summary>Why the file must not be read now, or null when it may.</summary>
    private static FileFingerprint? Describe(string full, ExpectedFile expected)
    {
        var info = new FileInfo(full);
        if (!info.Exists)
        {
            return FileFingerprint.Refused(FingerprintStatus.Missing, "It is no longer there.");
        }

        if (IsLink(info))
        {
            return FileFingerprint.Refused(FingerprintStatus.Link, "It is a shortcut to somewhere else, so DeskAI did not follow it.");
        }

        if ((info.Attributes & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0)
        {
            return FileFingerprint.Refused(FingerprintStatus.OnlineOnly, "It is stored online only. Reading it would download it, so DeskAI did not.");
        }

        return info.Length != expected.SizeBytes || info.LastWriteTimeUtc != expected.ModifiedAtUtc.UtcDateTime
            ? FileFingerprint.Refused(FingerprintStatus.Changed, "It changed since DeskAI last looked. Refresh the folder in Search, then check again.")
            : null;
    }

    private static bool CrossesLink(string rootPath, string full)
    {
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (Directory.Exists(current) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
        {
            return true;
        }

        var folders = Path.GetRelativePath(current, Path.GetDirectoryName(full)!)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment != ".");
        foreach (var segment in folders)
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLink(FileInfo info) =>
        info.Exists && ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null);

    /// <summary>The file inside the folder, or null if the path would land outside it.</summary>
    private static string? ResolveInsideRoot(string rootPath, string relativePath)
    {
        try
        {
            var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
            var full = Path.GetFullPath(Path.Combine(canonicalRoot, relativePath));
            return full.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
