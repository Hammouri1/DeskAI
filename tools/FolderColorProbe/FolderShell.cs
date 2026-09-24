using System.Runtime.InteropServices;

namespace DeskAI.FolderColorProbe;

/// <summary>
/// Windows' own folder-customization calls: set a folder's icon (which writes its
/// <c>desktop.ini</c> and marks the folder), ask which icon the shell would show, and tell
/// Explorer a folder changed.
/// </summary>
internal static class FolderShell
{
    private const uint FcsForceWrite = 0x2;
    private const uint FcsmIconFile = 0x10;
    private const uint ShgfiIconLocation = 0x1000;
    private const int ShcneUpdateDir = 0x1000;
    private const int ShcneUpdateItem = 0x2000;
    private const uint ShcnfPathW = 0x5;
    private const uint ShcnfFlush = 0x1000;

    internal static void SetIcon(string folder, string iconFile, int index)
    {
        var icon = Marshal.StringToHGlobalUni(iconFile);
        try
        {
            var settings = new FolderCustomSettings
            {
                Size = (uint)Marshal.SizeOf<FolderCustomSettings>(),
                Mask = FcsmIconFile,
                IconFile = icon,
                IconFileLength = 0,
                IconIndex = index,
            };
            Marshal.ThrowExceptionForHR(SHGetSetFolderCustomSettings(ref settings, folder, FcsForceWrite));
        }
        finally
        {
            Marshal.FreeHGlobal(icon);
        }
    }

    /// <returns>The icon file and index the shell would use, as "path,index".</returns>
    internal static string IconLocation(string path)
    {
        var info = default(FileInfo);
        return SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<FileInfo>(), ShgfiIconLocation) == IntPtr.Zero
            ? "(no answer)"
            : $"{info.DisplayName},{info.IconIndex}";
    }

    internal static void Changed(string folder)
    {
        SHChangeNotify(ShcneUpdateItem, ShcnfPathW | ShcnfFlush, folder, IntPtr.Zero);
        SHChangeNotify(ShcneUpdateDir, ShcnfPathW | ShcnfFlush, folder, IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FolderCustomSettings
    {
        public uint Size;
        public uint Mask;
        public IntPtr ViewId;
        public IntPtr WebViewTemplate;
        public uint WebViewTemplateLength;
        public IntPtr WebViewTemplateVersion;
        public IntPtr InfoTip;
        public uint InfoTipLength;
        public IntPtr ClassId;
        public uint Flags;
        public IntPtr IconFile;
        public uint IconFileLength;
        public int IconIndex;
        public IntPtr Logo;
        public uint LogoLength;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHGetSetFolderCustomSettings(ref FolderCustomSettings settings, string path, uint readWrite);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfoW", ExactSpelling = true)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref FileInfo info, uint size, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern void SHChangeNotify(int eventId, uint flags, string path, IntPtr unused);
}
