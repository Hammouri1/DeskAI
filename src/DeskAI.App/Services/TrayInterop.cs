using System.Runtime.InteropServices;

namespace DeskAI.App.Services;

/// <summary>
/// The Win32 calls behind <see cref="TrayPresence"/>, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Separated from <see cref="TrayPresence"/> so the adapter's behaviour can be read without
/// wading through declarations. Nothing here makes a decision, keeps state, or touches a file:
/// these are the shell's window, menu, and notification-area entry points only. There is
/// deliberately no process-launch, registry, service, or permission call in this file, and adding
/// one would put it within reach of code that runs with no window on screen.
/// </para>
/// <para>
/// These are classic <see cref="DllImportAttribute"/> declarations rather than source-generated
/// ones. The source generator emits <c>unsafe</c> stubs for every by-reference or string argument
/// and so requires <c>AllowUnsafeBlocks</c>, which this app does not enable; the same reasoning
/// already applies in <c>WindowsCredentialVault</c>. Every string here is explicitly UTF-16, and
/// every struct is laid out sequentially with its inline text fields declared as
/// <see cref="UnmanagedType.ByValTStr"/> so the runtime marshaller produces exactly the native
/// layout the shell validates <c>cbSize</c> against.
/// </para>
/// </remarks>
internal static class TrayInterop
{
    internal const uint WM_NULL = 0x0000;
    internal const uint WM_QUERYENDSESSION = 0x0011;
    internal const uint WM_ENDSESSION = 0x0016;
    internal const uint WM_CONTEXTMENU = 0x007B;
    internal const uint WM_LBUTTONUP = 0x0202;
    internal const uint WM_RBUTTONUP = 0x0205;

    /// <summary>An ordinary top-level window, which is never shown. See <see cref="TrayPresence"/>.</summary>
    internal const uint WS_OVERLAPPED = 0x0000_0000;

    internal const uint NIM_ADD = 0x0000_0000;
    internal const uint NIM_MODIFY = 0x0000_0001;
    internal const uint NIM_DELETE = 0x0000_0002;

    internal const uint NIF_MESSAGE = 0x0000_0001;
    internal const uint NIF_ICON = 0x0000_0002;
    internal const uint NIF_TIP = 0x0000_0004;

    internal const uint MF_STRING = 0x0000_0000;
    internal const uint MF_CHECKED = 0x0000_0008;

    internal const uint TPM_RIGHTBUTTON = 0x0000_0002;
    internal const uint TPM_RETURNCMD = 0x0000_0100;

    internal const uint IMAGE_ICON = 1;
    internal const uint LR_DEFAULTSIZE = 0x0000_0040;
    internal const uint LR_SHARED = 0x0000_8000;

    /// <summary>The class name is registered once and already exists on any second attempt.</summary>
    internal const int ERROR_CLASS_ALREADY_EXISTS = 1410;

    /// <summary>The length of <c>NOTIFYICONDATAW.szTip</c>, in characters, including its terminator.</summary>
    internal const int TipLength = 128;

    /// <summary>The <c>WNDPROC</c> signature. Kept alive by a field, or the shell calls freed memory.</summary>
    internal delegate nint WindowProcedure(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassExW(ref WindowClassExW windowClass);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindowExW(
        uint extendedStyle,
        string className,
        string? windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", EntryPoint = "DestroyWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode)]
    internal static extern nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint RegisterWindowMessageW(string name);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "GetCursorPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", EntryPoint = "CreatePopupMenu", SetLastError = true)]
    internal static extern nint CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AppendMenuW(nint menu, uint flags, nuint itemId, string item);

    [DllImport("user32.dll", EntryPoint = "SetMenuDefaultItem", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetMenuDefaultItem(nint menu, uint item, [MarshalAs(UnmanagedType.Bool)] bool byPosition);

    [DllImport("user32.dll", EntryPoint = "TrackPopupMenuEx", SetLastError = true)]
    internal static extern int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);

    [DllImport("user32.dll", EntryPoint = "DestroyMenu", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll", EntryPoint = "LoadIconW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint LoadIconW(nint instance, nint iconName);

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint LoadImageW(nint instance, nint name, uint type, int cx, int cy, uint load);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Shell_NotifyIconW(uint message, ref NotifyIconDataW data);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClassExW
    {
        public uint Size;
        public uint Style;
        public nint WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public nint MenuName;
        public nint ClassName;
        public nint SmallIcon;
    }

    /// <summary>
    /// The current <c>NOTIFYICONDATAW</c>. The unused text fields are still declared, because the
    /// shell validates <c>cbSize</c> against a known layout and a short struct is rejected outright.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyIconDataW
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = TipLength)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint VersionOrTimeout;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid ItemGuid;
        public nint BalloonIcon;
    }
}
