namespace DeskAI.Core.QuickSearch;

/// <summary>What DeskAI says about quick search outside the bar: the icon's tooltip.</summary>
/// <remarks>The tooltip field holds 128 characters; the longest checking tooltip plus the suffix stays well under.</remarks>
public static class QuickSearchWords
{
    public const string Tooltip = "DeskAI — press Ctrl + Alt + Space to find a file";

    public static string WithChecking(string checkingTooltip) => checkingTooltip + " · Ctrl + Alt + Space finds a file";
}
