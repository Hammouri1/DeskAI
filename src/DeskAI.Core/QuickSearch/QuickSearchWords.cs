namespace DeskAI.Core.QuickSearch;

/// <summary>What DeskAI says about quick search outside the bar: the icon's tooltip, naming the chosen shortcut.</summary>
/// <remarks>The tooltip field holds 128 characters; the longest checking tooltip plus the longest suffix stays well under.</remarks>
public static class QuickSearchWords
{
    public static string Tooltip(QuickSearchShortcut shortcut) => $"DeskAI — press {QuickSearchShortcuts.Text(shortcut)} to find a file";

    public static string WithChecking(string checkingTooltip, QuickSearchShortcut shortcut) =>
        $"{checkingTooltip} · {QuickSearchShortcuts.Text(shortcut)} finds a file";
}
