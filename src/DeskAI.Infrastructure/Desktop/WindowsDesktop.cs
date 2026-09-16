using System.Runtime.InteropServices;
using System.Text;
using DeskAI.Core.Abstractions;

namespace DeskAI.Infrastructure.Desktop;

/// <summary>
/// Reads and sets the Windows wallpaper picture through <c>SystemParametersInfo</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the documented Windows call for the wallpaper. With <c>SPIF_UPDATEINIFILE</c>
/// Windows itself persists the choice; DeskAI writes no registry value of its own, and a
/// source-scanning test keeps registry APIs out of the code. Nothing else in this file: no
/// theme, accent, lock screen, or shell call.
/// </para>
/// <para>
/// Classic <see cref="DllImportAttribute"/> for the same reason as the credential vault and
/// the tray: the app does not enable unsafe code.
/// </para>
/// </remarks>
public sealed class WindowsWallpaperSetter : IWallpaperSetter
{
    private const uint SpiGetDeskWallpaper = 0x0073;
    private const uint SpiSetDeskWallpaper = 0x0014;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;
    private const int MaxPath = 260;

    public string? ReadCurrent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var buffer = new StringBuilder(MaxPath);
        return SystemParametersInfoGet(SpiGetDeskWallpaper, (uint)buffer.Capacity, buffer, 0)
            ? buffer.ToString()
            : null;
    }

    public void Set(string imagePath)
    {
        ArgumentNullException.ThrowIfNull(imagePath);
        if (!OperatingSystem.IsWindows() ||
            !SystemParametersInfoSet(SpiSetDeskWallpaper, 0, imagePath, SpifUpdateIniFile | SpifSendChange))
        {
            throw new InvalidOperationException("Windows did not let DeskAI change the wallpaper.");
        }
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoGet(uint action, uint parameter, StringBuilder value, uint flags);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoSet(uint action, uint parameter, string value, uint flags);
}

/// <summary>Looks at a picked picture's kind and size without opening it.</summary>
public sealed class FilePictureInspector : IPictureInspector
{
    public PictureFacts? Inspect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            if (Directory.Exists(path))
            {
                var attributes = File.GetAttributes(path);
                return new PictureFacts(false, attributes.HasFlag(FileAttributes.ReparsePoint), 0);
            }

            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }

            return new PictureFacts(true, info.Attributes.HasFlag(FileAttributes.ReparsePoint), info.Length);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>The person's own folders, asked from Windows rather than guessed from a user name.</summary>
public sealed class WindowsKnownFolders : IKnownFolders
{
    public string? Desktop
    {
        get
        {
            var path = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
    }
}
