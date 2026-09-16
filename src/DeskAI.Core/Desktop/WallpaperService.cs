using DeskAI.Core.Abstractions;

namespace DeskAI.Core.Desktop;

/// <summary>A picked picture, checked, and what Windows shows now. Changes nothing.</summary>
public sealed record WallpaperPreview(string Path, string Name, string CurrentDescription, string? Problem)
{
    public bool CanUse => Problem is null;
}

/// <summary>What pressing Use as wallpaper, or Put back, did.</summary>
public sealed record WallpaperOutcome(bool Changed, string Summary);

/// <summary>The wallpaper DeskAI replaced, so it can be put back.</summary>
/// <param name="WindowsShowsSomethingElse">
/// Windows now shows a picture other than the one DeskAI set, so the person changed it since.
/// Said before Put back is pressed, because Put back restores the recorded one regardless.
/// </param>
public sealed record WallpaperRestore(string PreviousDescription, bool WindowsShowsSomethingElse);

/// <summary>
/// Makes a picture the person picked their wallpaper, and puts the old one back.
/// </summary>
/// <remarks>
/// <para>
/// This is DeskAI's first change to a Windows setting, so it is kept narrow. The picture path
/// comes only from the person, through the Windows file dialog; it must be a plain local file
/// with a picture extension, not a link, not on a network, at most <see cref="MaxBytes"/>; and
/// the check is run again at the moment of use. DeskAI never opens the file.
/// </para>
/// <para>
/// The wallpaper that was there before is written down <em>before</em> the change is made, in
/// its own row, so a crash between the two still leaves it recorded and Put back still works
/// after DeskAI is reopened. A plain-colour desktop is recorded as empty and restored as such.
/// </para>
/// <para>
/// It holds the setter, the inspector, and the settings store, and nothing else: no scanner,
/// reader, executor, AI, or rule type. A test fails if one is added.
/// </para>
/// </remarks>
public sealed class WallpaperService(IWallpaperSetter setter, IPictureInspector pictures, IAppSettingsStore store)
{
    public const long MaxBytes = 50L * 1024 * 1024;

    public const string PreviousKey = "wallpaper.previous";
    public const string SetKey = "wallpaper.set";

    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".bmp"];

    /// <summary>Checks the picked picture and says what Windows shows now. Changes nothing.</summary>
    public Task<WallpaperPreview> PreviewAsync(string? path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var problem = Check(path);
        return Task.FromResult(new WallpaperPreview(
            path ?? string.Empty,
            problem is null ? System.IO.Path.GetFileName(path!) : string.Empty,
            Describe(setter.ReadCurrent()),
            problem));
    }

    /// <summary>Makes the previewed picture the wallpaper, after the person agreed.</summary>
    public async Task<WallpaperOutcome> UseAsync(WallpaperPreview preview, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (Check(preview.Path) is { } problem)
        {
            return new(false, problem);
        }

        var current = setter.ReadCurrent();
        if (current is null)
        {
            return new(false, "DeskAI could not tell what your wallpaper is now, so it did not change it.");
        }

        // What the person had is recorded first, so Put back survives a stop right after the
        // change. If they changed the wallpaper in Windows since DeskAI last set it, that newer
        // choice is the one worth remembering.
        var previous = await store.ReadAsync(PreviousKey, cancellationToken).ConfigureAwait(false);
        var lastSet = await store.ReadAsync(SetKey, cancellationToken).ConfigureAwait(false);
        var recordedNow = previous is null || !SamePath(current, lastSet);
        if (recordedNow)
        {
            await store.WriteAsync(PreviousKey, current, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            setter.Set(preview.Path);
        }
        catch (InvalidOperationException exception)
        {
            // Nothing changed, so a Put back offer for it would be a lie.
            if (recordedNow && previous is null)
            {
                await store.RemoveAsync(PreviousKey, cancellationToken).ConfigureAwait(false);
            }
            else if (recordedNow)
            {
                await store.WriteAsync(PreviousKey, previous!, cancellationToken).ConfigureAwait(false);
            }

            return new(false, exception.Message);
        }

        await store.WriteAsync(SetKey, preview.Path, cancellationToken).ConfigureAwait(false);
        return new(true, $"{preview.Name} is now your wallpaper.");
    }

    /// <summary>The wallpaper DeskAI replaced, if there is one to put back.</summary>
    public async Task<WallpaperRestore?> FindRestoreAsync(CancellationToken cancellationToken = default)
    {
        var previous = await store.ReadAsync(PreviousKey, cancellationToken).ConfigureAwait(false);
        if (previous is null)
        {
            return null;
        }

        var lastSet = await store.ReadAsync(SetKey, cancellationToken).ConfigureAwait(false);
        var current = setter.ReadCurrent();
        return new WallpaperRestore(Describe(previous), current is not null && lastSet is not null && !SamePath(current, lastSet));
    }

    /// <summary>Puts the recorded wallpaper back, if its file is still there.</summary>
    public async Task<WallpaperOutcome> PutBackAsync(CancellationToken cancellationToken = default)
    {
        var previous = await store.ReadAsync(PreviousKey, cancellationToken).ConfigureAwait(false);
        if (previous is null)
        {
            return new(false, "There is no old wallpaper to put back.");
        }

        if (previous.Length > 0 && pictures.Inspect(previous) is not { IsPlainFile: true, IsLink: false })
        {
            return new(false, "Your old wallpaper file is no longer there, so nothing was changed.");
        }

        try
        {
            setter.Set(previous);
        }
        catch (InvalidOperationException exception)
        {
            return new(false, exception.Message);
        }

        await store.RemoveAsync(PreviousKey, cancellationToken).ConfigureAwait(false);
        await store.RemoveAsync(SetKey, cancellationToken).ConfigureAwait(false);
        return new(true, previous.Length == 0
            ? "Your wallpaper is a plain colour again."
            : $"{System.IO.Path.GetFileName(previous)} is your wallpaper again.");
    }

    /// <summary>Why the path cannot be used as a wallpaper, or null when it can.</summary>
    private string? Check(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Choose a picture first.";
        }

        if (!System.IO.Path.IsPathFullyQualified(path) ||
            path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal) ||
            path.Contains("://", StringComparison.Ordinal))
        {
            return "The picture must be a file on this computer.";
        }

        if (!Extensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
        {
            return "DeskAI can use JPG, PNG, and BMP pictures.";
        }

        return pictures.Inspect(path) switch
        {
            null => "That picture is no longer there.",
            { IsLink: true } => "That picture is a link or shortcut, so it was left alone.",
            { IsPlainFile: false } => "That is not a picture file.",
            { SizeBytes: 0 } => "That picture file is empty.",
            { SizeBytes: > MaxBytes } => "That picture is bigger than 50 MB.",
            _ => null,
        };
    }

    private static string Describe(string? path) => path switch
    {
        null => "DeskAI could not tell",
        "" => "a plain colour",
        _ => System.IO.Path.GetFileName(path),
    };

    private static bool SamePath(string? first, string? second) =>
        first is not null && second is not null &&
        string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);
}
