using System.Text.RegularExpressions;
using DeskAI.App.Help;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// The "?" explanations are for people who are not technical, so they are held to rules a
/// test can check: every part filled in, short, and free of the words the UI keeps out.
/// </summary>
public sealed class HelpCatalogTests
{
    public static TheoryData<string> TopicIds()
    {
        var data = new TheoryData<string>();
        foreach (var topic in HelpCatalog.All)
        {
            data.Add(topic.Id);
        }

        return data;
    }

    [Fact]
    public void Every_topic_has_a_unique_id()
    {
        var ids = HelpCatalog.All.Select(topic => topic.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(TopicIds))]
    public void Every_topic_has_all_three_parts(string id)
    {
        var topic = HelpCatalog.Find(id)!;
        Assert.False(string.IsNullOrWhiteSpace(topic.Title));
        Assert.False(string.IsNullOrWhiteSpace(topic.WhatItIs));
        Assert.False(string.IsNullOrWhiteSpace(topic.WhatItDoes));
        Assert.False(string.IsNullOrWhiteSpace(topic.WhatItNeverDoes));
    }

    [Theory]
    [MemberData(nameof(TopicIds))]
    public void Every_part_stays_short(string id)
    {
        var topic = HelpCatalog.Find(id)!;
        Assert.InRange(Words(topic.WhatItIs), 1, HelpCatalog.MaxWhatItIsWords);
        Assert.InRange(Words(topic.WhatItDoes), 1, HelpCatalog.MaxWhatItDoesWords);
        Assert.InRange(Words(topic.WhatItNeverDoes), 1, HelpCatalog.MaxWhatItNeverDoesWords);
    }

    [Theory]
    [MemberData(nameof(TopicIds))]
    public void No_topic_uses_technical_words(string id)
    {
        var topic = HelpCatalog.Find(id)!;
        var text = string.Join(' ', topic.Title, topic.WhatItIs, topic.WhatItDoes, topic.WhatItNeverDoes);
        foreach (var word in HelpCatalog.BannedWords)
        {
            Assert.False(
                Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase),
                $"'{id}' uses the technical word '{word}'.");
        }
    }

    [Theory]
    [InlineData("home.health")]
    [InlineData("home.duplicates")]
    [InlineData("home.storage")]
    [InlineData("organize.practice")]
    [InlineData("organize.tidy")]
    [InlineData("organize.permission")]
    [InlineData("organize.suggestions")]
    [InlineData("organize.leftAlone")]
    [InlineData("search.searching")]
    [InlineData("search.connect")]
    [InlineData("search.readInside")]
    [InlineData("search.saved")]
    [InlineData("automation.checking")]
    [InlineData("automation.frequency")]
    [InlineData("automation.pause")]
    [InlineData("automation.notifications")]
    [InlineData("automation.rules")]
    [InlineData("automation.practice")]
    [InlineData("automation.sentence")]
    [InlineData("settings.sharing")]
    [InlineData("settings.aiChoice")]
    [InlineData("settings.key")]
    [InlineData("settings.dailyLimit")]
    [InlineData("shell.scope")]
    public void Each_feature_the_design_names_has_help(string id) =>
        Assert.NotNull(HelpCatalog.Find(id));

    [Fact]
    public void An_unknown_topic_is_not_found() =>
        Assert.Null(HelpCatalog.Find("no.such.topic"));

    [Fact]
    public void No_help_text_suggests_a_connected_folder_can_be_changed()
    {
        foreach (var topic in HelpCatalog.All)
        {
            var text = string.Join(' ', topic.WhatItIs, topic.WhatItDoes);
            Assert.DoesNotContain("tidy your", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("moves your", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static int Words(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
}
