using DeskAI.App.Services;
using DeskAI.Core.QuickSearch;

namespace DeskAI.Presentation.Tests;

/// <summary>Each choice is one fixed combination, held down reports once, and no other key can be named.</summary>
public sealed class QuickSearchHotKeysTests
{
    [Theory]
    [InlineData(QuickSearchShortcut.CtrlAltD, 0x4003u, 0x44u)]
    [InlineData(QuickSearchShortcut.CtrlAltSpace, 0x4003u, 0x20u)]
    [InlineData(QuickSearchShortcut.CtrlShiftSpace, 0x4006u, 0x20u)]
    public void Each_choice_is_one_fixed_combination_without_repeat(QuickSearchShortcut shortcut, uint modifiers, uint key) =>
        Assert.Equal((modifiers, key), QuickSearchHotKeys.For(shortcut));

    [Fact]
    public void The_list_holds_exactly_three_shortcuts() =>
        Assert.Equal(3, Enum.GetValues<QuickSearchShortcut>().Length);
}
