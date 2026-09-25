namespace DeskAI.Core.QuickSearch;

/// <summary>
/// The shortcuts quick search can listen for (the owner's choice, 2026-09-25). A closed list, so a
/// stored value can never name any other key. The order is the order of the drop-down.
/// </summary>
public enum QuickSearchShortcut { CtrlAltD = 0, CtrlAltSpace = 1, CtrlShiftSpace = 2 }

/// <summary>The words for each shortcut, and which one a fresh DeskAI uses.</summary>
/// <remarks>Ctrl + Alt + D is the default because Ctrl + Alt + Space clashed with the Claude desktop app on the owner's PC.</remarks>
public static class QuickSearchShortcuts
{
    public static QuickSearchShortcut Default => QuickSearchShortcut.CtrlAltD;

    public static IReadOnlyList<QuickSearchShortcut> All { get; } = Enum.GetValues<QuickSearchShortcut>();

    /// <summary>What a person reads, for example "Ctrl + Alt + D".</summary>
    public static string Text(QuickSearchShortcut shortcut) => shortcut switch
    {
        QuickSearchShortcut.CtrlAltSpace => "Ctrl + Alt + Space",
        QuickSearchShortcut.CtrlShiftSpace => "Ctrl + Shift + Space",
        _ => "Ctrl + Alt + D",
    };
}
