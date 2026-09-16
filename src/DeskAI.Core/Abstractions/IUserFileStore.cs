namespace DeskAI.Core.Abstractions;

/// <summary>
/// Reads or writes one text file at a path the person chose in a Windows dialog.
/// </summary>
/// <remarks>
/// Used only for the backup file. The path always comes from the Windows save or open dialog,
/// never from DeskAI, a rule, or AI; the implementation still refuses anything that is not a
/// plain local <c>.json</c> file, and reads are bounded.
/// </remarks>
public interface IUserFileStore
{
    Task WriteTextAsync(string path, string text, CancellationToken cancellationToken = default);

    /// <exception cref="InvalidOperationException">The file cannot be used, with a reason a person can read.</exception>
    Task<string> ReadTextAsync(string path, int maximumBytes, CancellationToken cancellationToken = default);
}
