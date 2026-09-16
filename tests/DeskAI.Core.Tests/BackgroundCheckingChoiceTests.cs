using DeskAI.Core.Rules;

namespace DeskAI.Core.Tests;

/// <summary>
/// The promises this feature makes, in the words it makes them. They are computed rather
/// than fixed so a mode can never be described by a sentence that stopped being true.
/// </summary>
public sealed class BackgroundCheckingChoiceTests
{
    [Fact]
    public void The_question_says_it_does_not_add_itself_to_Windows_startup()
    {
        var question = BackgroundCheckingChoice.Ask(AutomaticCheckSettings.Default);

        Assert.Contains("Windows startup", question.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_question_states_the_limit_that_a_check_cannot_move_anything()
    {
        var question = BackgroundCheckingChoice.Ask(AutomaticCheckSettings.Default);

        Assert.Contains("cannot move", question.LimitLine, StringComparison.Ordinal);
        Assert.Contains("does not tidy while you are away", question.LimitLine, StringComparison.Ordinal);
    }

    [Fact]
    public void The_question_says_what_happens_when_notifications_are_off()
    {
        var question = BackgroundCheckingChoice.Ask(AutomaticCheckSettings.Default);

        Assert.False(question.NotifyWhenSomethingIsFound);
        Assert.Contains("next time you open DeskAI", question.NotifyCaption, StringComparison.Ordinal);
    }

    [Fact]
    public void The_question_carries_the_notification_choice_already_stored()
    {
        var settings = AutomaticCheckSettings.Default with { NotifyWhenSomethingIsFound = true };

        Assert.True(BackgroundCheckingChoice.Ask(settings).NotifyWhenSomethingIsFound);
    }

    [Fact]
    public void The_tooltip_says_how_often_it_looks()
    {
        var settings = AutomaticCheckSettings.Default with
        {
            Mode = AutomaticCheckMode.InBackground,
            Frequency = AutomaticCheckFrequency.EveryHour,
        };

        Assert.Equal("DeskAI — looking every hour", BackgroundCheckingChoice.Tooltip(settings, filesToReview: null));
    }

    [Fact]
    public void The_tooltip_says_paused_when_it_is_paused()
    {
        var settings = AutomaticCheckSettings.Default with
        {
            Mode = AutomaticCheckMode.InBackground,
            IsPaused = true,
        };

        Assert.Equal("DeskAI — checks paused", BackgroundCheckingChoice.Tooltip(settings, filesToReview: null));
    }

    [Fact]
    public void The_tooltip_says_only_when_you_ask_rather_than_claiming_it_is_looking()
    {
        var settings = AutomaticCheckSettings.Default with
        {
            Mode = AutomaticCheckMode.InBackground,
            Frequency = AutomaticCheckFrequency.OnlyWhenIAsk,
        };

        Assert.Equal("DeskAI — only looks when you ask", BackgroundCheckingChoice.Tooltip(settings, filesToReview: null));
    }

    [Fact]
    public void The_tooltip_carries_a_count_and_never_a_name()
    {
        var settings = AutomaticCheckSettings.Default with { Mode = AutomaticCheckMode.InBackground };

        var tooltip = BackgroundCheckingChoice.Tooltip(settings, filesToReview: 3);

        Assert.Equal("DeskAI — 3 files to review", tooltip);
    }

    [Fact]
    public void The_tooltip_reads_naturally_for_one_file()
    {
        var settings = AutomaticCheckSettings.Default with { Mode = AutomaticCheckMode.InBackground };

        Assert.Equal("DeskAI — 1 file to review", BackgroundCheckingChoice.Tooltip(settings, filesToReview: 1));
    }

    [Fact]
    public void A_paused_DeskAI_never_shows_a_count_it_is_no_longer_keeping_current()
    {
        var settings = AutomaticCheckSettings.Default with
        {
            Mode = AutomaticCheckMode.InBackground,
            IsPaused = true,
        };

        Assert.Equal("DeskAI — checks paused", BackgroundCheckingChoice.Tooltip(settings, filesToReview: 3));
    }

    [Fact]
    public void The_tooltip_never_exceeds_what_Windows_will_show()
    {
        foreach (var frequency in Enum.GetValues<AutomaticCheckFrequency>())
        {
            var settings = AutomaticCheckSettings.Default with
            {
                Mode = AutomaticCheckMode.InBackground,
                Frequency = frequency,
            };

            // Shell_NotifyIcon truncates a tooltip at 128 characters including the
            // terminator, and a promise that is cut in half is worse than a shorter one.
            Assert.InRange(BackgroundCheckingChoice.Tooltip(settings, 999_999).Length, 1, 127);
        }
    }

    [Fact]
    public void More_details_says_checking_stops_on_close_only_when_that_is_true()
    {
        var open = BackgroundCheckingChoice.MoreDetails(AutomaticCheckMode.WhileAppIsOpen);
        var background = BackgroundCheckingChoice.MoreDetails(AutomaticCheckMode.InBackground);

        Assert.Contains("only while DeskAI is open", open, StringComparison.Ordinal);
        Assert.DoesNotContain("only while DeskAI is open", background, StringComparison.Ordinal);
    }

    [Fact]
    public void More_details_promises_no_Windows_startup_in_both_modes()
    {
        foreach (var mode in Enum.GetValues<AutomaticCheckMode>())
        {
            Assert.Contains(
                "does not add itself to Windows startup",
                BackgroundCheckingChoice.MoreDetails(mode),
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void More_details_says_a_check_moves_nothing_in_both_modes()
    {
        foreach (var mode in Enum.GetValues<AutomaticCheckMode>())
        {
            Assert.Contains(
                "does not move anything",
                BackgroundCheckingChoice.MoreDetails(mode),
                StringComparison.Ordinal);
        }
    }
}
