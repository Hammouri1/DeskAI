using System.Text;
using DeskAI.Core.Abstractions;

namespace DeskAI.Infrastructure.Files;

/// <summary>
/// The one place DeskAI writes a file a person asked for: the backup file, at the path they
/// picked in the Windows dialog.
/// </summary>
/// <remarks>
/// It accepts only a fully qualified local path ending in <c>.json</c>: no network share, no
/// relative path, no other extension. Reading refuses a link and a file over the caller's
/// bound before opening, and opens read-only. Writing creates or replaces the file the person
/// chose, which the save dialog has already asked them about.
/// </remarks>
public sealed class UserFileStore : IUserFileStore
{
    public async Task WriteTextAsync(string path, string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var checkedPath = Check(path);
        await File.WriteAllTextAsync(checkedPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string> ReadTextAsync(string path, int maximumBytes, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        var checkedPath = Check(path);
        var info = new FileInfo(checkedPath);
        if (!info.Exists)
        {
            throw new InvalidOperationException("That file is no longer there.");
        }

        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException("That file is a link, so DeskAI did not open it.");
        }

        if (info.Length > maximumBytes)
        {
            throw new InvalidOperationException("That file is too big to be a DeskAI backup.");
        }

        await using var stream = new FileStream(
            checkedPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Check(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("DeskAI can only use a file on this computer.");
        }

        if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A DeskAI backup is a .json file.");
        }

        return Path.GetFullPath(path);
    }
}
