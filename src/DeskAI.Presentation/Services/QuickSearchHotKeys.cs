using DeskAI.Core.QuickSearch;

namespace DeskAI.App.Services;

/// <summary>
/// The fixed Windows key values for each shortcut in the closed list (ADR 0047). Here rather than
/// in the App so a test can check the table; the App only passes these values to RegisterHotKey.
/// </summary>
public static class QuickSearchHotKeys
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;

    /// <summary>Holding the keys down reports once, not over and over.</summary>
    public const uint ModNoRepeat = 0x4000;

    public const uint KeySpace = 0x20;
    public const uint KeyD = 0x44;

    public static (uint Modifiers, uint Key) For(QuickSearchShortcut shortcut) => shortcut switch
    {
        QuickSearchShortcut.CtrlAltSpace => (ModControl | ModAlt | ModNoRepeat, KeySpace),
        QuickSearchShortcut.CtrlShiftSpace => (ModControl | ModShift | ModNoRepeat, KeySpace),
        _ => (ModControl | ModAlt | ModNoRepeat, KeyD),
    };
}
