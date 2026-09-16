namespace DeskAI.Core.Abstractions;

/// <summary>
/// The one Windows setting DeskAI can change: the desktop wallpaper picture.
/// </summary>
/// <remarks>
/// Deliberately two calls and no more. It cannot list pictures, read a file, or change any
/// other setting. Only <c>WallpaperService</c> may hold it, after the person picked a picture
/// and pressed the button; a reflection test keeps it out of anything that runs on its own.
/// </remarks>
public interface IWallpaperSetter
{
    /// <summary>
    /// The path of the picture Windows shows now; an empty string for a plain colour; null
    /// when Windows would not say.
    /// </summary>
    string? ReadCurrent();

    /// <summary>Makes <paramref name="imagePath"/> the wallpaper, or an empty string for a plain colour.</summary>
    /// <remarks>Named <c>Apply</c> rather than <c>Set</c>, which is a keyword in some languages (CA1716).</remarks>
    /// <exception cref="InvalidOperationException">Windows refused, with a plain reason.</exception>
    void Apply(string imagePath);
}

/// <summary>What is at a path a person picked as a picture, read without opening it.</summary>
public sealed record PictureFacts(bool IsPlainFile, bool IsLink, long SizeBytes);

/// <summary>Looks at one picked file's kind and size. Never reads what is inside it.</summary>
public interface IPictureInspector
{
    /// <returns>The facts, or null when nothing is at that path.</returns>
    PictureFacts? Inspect(string path);
}
