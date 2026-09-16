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
/// <remarks>
/// Desktop, Documents, and Pictures come from <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/>.
/// .NET has no special-folder value for Downloads, so that one is asked through the same
/// Windows known-folder call the others use underneath, <c>SHGetKnownFolderPath</c>, by its
/// documented ID. Nothing here reads the registry or builds a path from a user name.
/// </remarks>
public sealed class WindowsKnownFolders : IKnownFolders
{
    // FOLDERID_Downloads, from the Windows SDK's KnownFolders.h.
    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    public string? Desktop => Clean(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));

    public string? Downloads
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            var id = DownloadsFolderId;
            var result = SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var buffer);
            try
            {
                return result == 0 && buffer != IntPtr.Zero ? Clean(Marshal.PtrToStringUni(buffer)) : null;
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(buffer);
                }
            }
        }
    }

    public string? Documents => Clean(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

    public string? Pictures => Clean(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));

    private static string? Clean(string? path) => string.IsNullOrWhiteSpace(path) ? null : path;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}
