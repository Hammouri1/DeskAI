using System.Runtime.InteropServices;

namespace DeskAI.App.Services;

/// <summary>
/// The two Win32 calls behind <see cref="GlobalHotKey"/>, and nothing else. Windows reports only
/// the one registered combination; there is deliberately no keyboard hook here (ADR 0047).
/// </summary>
internal static class HotKeyInterop
{
    internal const uint WM_HOTKEY = 0x0312;
    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_NOREPEAT = 0x4000;
    internal const uint VK_SPACE = 0x20;
    internal const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    /// <summary>The parent that makes a window message-only: never shown, never in any list.</summary>
    internal const nint HWND_MESSAGE = -3;

    [DllImport("user32.dll", EntryPoint = "RegisterHotKey", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", EntryPoint = "UnregisterHotKey", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint window, int id);
}
