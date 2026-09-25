using System.Text.RegularExpressions;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// The quick search window and buddies, read from their source: where the bar sits, what the
/// screen reader hears, that every buddy has every mood and a still pose, and that the shortcut is
/// the agreed one registered without a keyboard hook.
/// </summary>
public sealed class QuickSearchLayoutTests
{
    private static readonly string[] BuddyFiles =
        ["SparkyBuddy.xaml", "ArchieBuddy.xaml", "PipBuddy.xaml", "FetchBuddy.xaml", "InkyBuddy.xaml", "MochiBuddy.xaml", "PaigeBuddy.xaml"];

    [Fact]
    public void The_shortcut_comes_from_the_fixed_list_one_at_a_time_without_a_keyboard_hook()
    {
        var hotKey = Read("src", "DeskAI.App", "Services", "GlobalHotKey.cs") + Read("src", "DeskAI.App", "Services", "HotKeyInterop.cs");

        Assert.Contains("RegisterHotKey", hotKey, StringComparison.Ordinal);
        Assert.Contains("QuickSearchHotKeys.For(shortcut)", hotKey, StringComparison.Ordinal);
        Assert.Contains("UnregisterHotKey(_window, HotKeyId)", hotKey, StringComparison.Ordinal);
        Assert.DoesNotContain("SetWindowsHookEx", hotKey, StringComparison.Ordinal);
        Assert.DoesNotContain("WH_KEYBOARD", hotKey, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAsyncKeyState", hotKey, StringComparison.Ordinal);
    }

    [Fact]
    public void The_bar_sits_top_centre_and_stays_out_of_the_taskbar_and_Alt_Tab()
    {
        var window = Read("src", "DeskAI.App", "Views", "QuickSearchWindow.xaml.cs");

        Assert.Contains("TopFraction = 0.12", window, StringComparison.Ordinal);
        Assert.Contains("IsShownInSwitchers = false", window, StringComparison.Ordinal);
        Assert.Contains("IsAlwaysOnTop = true", window, StringComparison.Ordinal);
        Assert.Contains("SetBorderAndTitleBar(false, false)", window, StringComparison.Ordinal);
    }

    [Fact]
    public void The_buddys_line_is_announced_politely_and_the_buddy_itself_is_decorative()
    {
        var xaml = Read("src", "DeskAI.App", "Views", "QuickSearchWindow.xaml");

        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AccessibilityView=\"Raw\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Find a file\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Snippets_are_plain_text()
    {
        var xaml = Read("src", "DeskAI.App", "Views", "QuickSearchWindow.xaml");

        Assert.Contains("Text=\"{x:Bind Snippet}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RichTextBlock", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Hyperlink", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Buddies))]
    public void Every_buddy_has_the_five_moods_their_moves_and_a_still_pose(string file)
    {
        var path = Path.Combine(RepositoryRoot(), "src", "DeskAI.App", "Views", "Buddies", file);
        if (!File.Exists(path))
        {
            Assert.Skip($"{file} is drawn in a later task.");
        }

        var xaml = File.ReadAllText(path);
        foreach (var mood in new[] { "Idle", "Thinking", "Found", "Nothing", "Happy" })
        {
            Assert.Contains($"<VisualState x:Name=\"{mood}\"", xaml, StringComparison.Ordinal);
            Assert.Contains($"<VisualState x:Name=\"{mood}Moving\"", xaml, StringComparison.Ordinal);
        }

        Assert.Contains("<VisualState x:Name=\"Still\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaElement", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaPlayer", xaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// A mood or a move that names a part the drawing does not have fails only when that mood is
    /// shown, on a person's screen. Every name a buddy's states use must exist in that buddy.
    /// </summary>
    [Theory]
    [MemberData(nameof(Buddies))]
    public void Every_mood_and_move_names_a_part_the_buddy_has(string file)
    {
        var xaml = Read("src", "DeskAI.App", "Views", "Buddies", file);
        var parts = Regex.Matches(xaml, "x:Name=\"(\\w+)\"").Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var used = Regex.Matches(xaml, "Target=\"(\\w+)\\.").Concat(Regex.Matches(xaml, "TargetName=\"(\\w+)\""))
            .Select(match => match.Groups[1].Value);

        Assert.All(used, name => Assert.Contains(name, parts));
    }

    [Fact]
    public void Each_buddy_you_choose_is_the_one_drawn()
    {
        var factory = Read("src", "DeskAI.App", "Views", "Buddies", "BuddyFactory.cs");

        foreach (var buddy in new[] { "Archie", "Pip", "Fetch", "Inky", "Mochi", "Paige" })
        {
            Assert.Contains($"SearchBuddy.{buddy} => new {buddy}Buddy(),", factory, StringComparison.Ordinal);
        }

        Assert.Contains("_ => new SparkyBuddy(),", factory, StringComparison.Ordinal);
    }

    [Fact]
    public void Moves_stop_when_Windows_animation_effects_are_off()
    {
        var control = Read("src", "DeskAI.App", "Views", "Buddies", "BuddyControl.cs");

        Assert.Contains("AnimationsEnabled", control, StringComparison.Ordinal);
        Assert.Contains("\"Still\"", control, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_quick_search_bar_notice_is_skipped_when_quick_search_alone_keeps_DeskAI()
    {
        var window = Read("src", "DeskAI.App", "MainWindow.xaml.cs");

        Assert.Contains("QuickSearchKeepsItRunning", window, StringComparison.Ordinal);
    }

    [Fact]
    public void The_icon_menu_offers_Find_a_file_and_Pause_only_when_offered()
    {
        var tray = Read("src", "DeskAI.App", "Services", "TrayPresence.cs");

        Assert.Contains("\"Find a file\"", tray, StringComparison.Ordinal);
        Assert.Contains("_menu.OffersPause", tray, StringComparison.Ordinal);
        Assert.Contains("_menu.OffersFind", tray, StringComparison.Ordinal);
    }

    /// <summary>
    /// The bar is a second window that is only ever hidden; if it outlived the main window, DeskAI
    /// would keep running with nothing on screen and no icon to quit from.
    /// </summary>
    [Fact]
    public void Closing_the_main_window_for_real_closes_the_bar_and_stops_the_shortcut()
    {
        var app = Read("src", "DeskAI.App", "App.xaml.cs");

        Assert.Contains("window.Closed +=", app, StringComparison.Ordinal);
        Assert.Contains("bar.Close();", app, StringComparison.Ordinal);
        Assert.Contains("Listen(false, ", app, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Quick_search_card_and_the_welcome_picture_are_placed()
    {
        var workspace = Read("src", "DeskAI.App", "Views", "WorkspacePage.xaml");
        Assert.Contains("Header=\"{x:Bind ViewModel.QuickSearch.SwitchHeader, Mode=OneWay}\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Header=\"Shortcut\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Text=\"Your search buddy\"", workspace, StringComparison.Ordinal);
        Assert.Contains("Topic=\"workspace.quicksearch\"", workspace, StringComparison.Ordinal);
        Assert.Contains("welcome.Current.ShowsBuddy", Read("src", "DeskAI.App", "Views", "WelcomeDialog.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Owner-found 2026-09-25: the tip sat inside Home's welcome panel and covered it. The owner
    /// said it was not needed, so Home and Search carry no quick search tip at all.
    /// </summary>
    [Fact]
    public void Home_and_Search_carry_no_quick_search_tip()
    {
        foreach (var page in new[] { "DashboardPage.xaml", "SearchPage.xaml" })
        {
            var xaml = Read("src", "DeskAI.App", "Views", page);
            Assert.DoesNotContain("QuickSearchTip", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("Ctrl + Alt + Space", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("Ctrl + Alt + D", xaml, StringComparison.Ordinal);
        }
    }

    public static TheoryData<string> Buddies() => new(BuddyFiles);

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepositoryRoot(), .. parts]));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DeskAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
