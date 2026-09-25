using System.Text.RegularExpressions;
using DeskAI.App.Help;
using DeskAI.Core.QuickSearch;

namespace DeskAI.Presentation.Tests;

/// <summary>Each buddy speaks in its own short voice and never in technical words.</summary>
public sealed class SearchBuddyLinesTests
{
    public static TheoryData<SearchBuddy, BuddyMood> Every()
    {
        var data = new TheoryData<SearchBuddy, BuddyMood>();
        foreach (var buddy in Enum.GetValues<SearchBuddy>())
        {
            foreach (var mood in Enum.GetValues<BuddyMood>())
            {
                data.Add(buddy, mood);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void Every_line_is_short_and_plain(SearchBuddy buddy, BuddyMood mood)
    {
        var line = SearchBuddyLines.Line(buddy, mood, found: 12);

        Assert.InRange(line.Length, 1, SearchBuddyLines.MaxLength);
        foreach (var word in HelpCatalog.BannedWords)
        {
            Assert.False(Regex.IsMatch(line, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase), $"{buddy} {mood}: '{word}'");
        }
    }

    [Fact]
    public void Each_buddy_has_its_own_greeting_found_nothing_and_clicked_lines()
    {
        foreach (var mood in new[] { BuddyMood.Idle, BuddyMood.Found, BuddyMood.Nothing, BuddyMood.Happy })
        {
            var lines = Enum.GetValues<SearchBuddy>().Select(buddy => SearchBuddyLines.Line(buddy, mood, 2)).ToArray();
            Assert.Equal(lines.Length, lines.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void The_lines_are_the_agreed_ones()
    {
        Assert.Equal("Hi! What are we looking for?", SearchBuddyLines.Line(SearchBuddy.Sparky, BuddyMood.Idle));
        Assert.Equal("Ah, 3 in the archives.", SearchBuddyLines.Line(SearchBuddy.Archie, BuddyMood.Found, 3));
        Assert.Equal("Scan done: no match.", SearchBuddyLines.Line(SearchBuddy.Pip, BuddyMood.Nothing));
        Assert.Equal("Wag wag!", SearchBuddyLines.Line(SearchBuddy.Fetch, BuddyMood.Happy));
        Assert.Equal("Reaching…", SearchBuddyLines.Line(SearchBuddy.Inky, BuddyMood.Thinking));
        Assert.Equal("Yay, 2 found!", SearchBuddyLines.Line(SearchBuddy.Mochi, BuddyMood.Found, 2));
        Assert.Equal("Not a ghost of a match.", SearchBuddyLines.Line(SearchBuddy.Paige, BuddyMood.Nothing));
    }

    [Fact]
    public void One_found_reads_right()
    {
        Assert.Equal("Found 1!", SearchBuddyLines.Line(SearchBuddy.Sparky, BuddyMood.Found, 1));
        Assert.Equal("Scan done: 1 found.", SearchBuddyLines.Line(SearchBuddy.Pip, BuddyMood.Found, 1));
    }

    [Fact]
    public void Each_buddy_has_its_full_name()
    {
        Assert.Equal(
            ["Sparky", "Archie the owl", "Pip the robot", "Fetch the fox", "Inky the octopus", "Mochi", "Paige the paper ghost"],
            Enum.GetValues<SearchBuddy>().Select(SearchBuddyLines.Name));
    }

    [Fact]
    public void Sparky_is_the_first_and_default_buddy() =>
        Assert.Equal(SearchBuddy.Sparky, Enum.GetValues<SearchBuddy>()[0]);
}
