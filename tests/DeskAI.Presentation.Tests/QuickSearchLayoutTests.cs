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
    public void The_bar_is_sized_for_the_scale_of_the_screen_it_opens_on_and_refits_when_that_scale_changes()
    {
        // Owner-found (2026-09-26, two screens at 100% and 125%): the first opening on the other
        // screen was sized with the scale of the screen the bar last stood on, so it was cut off
        // on the 125% screen and too wide on the 100% one until typing refitted it.
        var window = Read("src", "DeskAI.App", "Views", "QuickSearchWindow.xaml.cs");
        var scale = window[window.IndexOf("private double Scale()", StringComparison.Ordinal)..];
        scale = scale[..scale.IndexOf("private void OnXamlRootChanged", StringComparison.Ordinal)];

        Assert.Contains("GetDpiForMonitor", scale, StringComparison.Ordinal);
        Assert.Contains("MonitorFromPoint", scale, StringComparison.Ordinal);
        Assert.DoesNotContain("RasterizationScale", scale, StringComparison.Ordinal);
        Assert.DoesNotContain("GetDpiForWindow", scale, StringComparison.Ordinal);
        Assert.Contains("AppWindow.MoveAndResize(", window, StringComparison.Ordinal);
        Assert.DoesNotContain("AppWindow.ResizeClient(", window, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"XamlRoot\.Changed \+=", RegexOptions.None, TimeSpan.FromSeconds(1)), window);
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
    public void The_bar_has_a_search_icon_an_Esc_hint_and_an_edge_that_turns_only_while_buddies_may_move()
    {
        var xaml = Read("src", "DeskAI.App", "Views", "QuickSearchWindow.xaml");
        var code = Read("src", "DeskAI.App", "Views", "QuickSearchWindow.xaml.cs");

        Assert.Contains("Glyph=\"&#xE721;\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Esc\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"EdgeAngle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"20\"", xaml, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"if \(_motion\.IsOn\)\s*\{\s*_edgeTurn\.Begin\(\);", RegexOptions.None, TimeSpan.FromSeconds(1)), code);
        Assert.Contains("_edgeTurn.Stop();", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// Owner-found 2026-09-25 in the see-through probe: the card was 95% opaque, so text behind it
    /// showed through the middle of the box. Only the glow around the card may be see-through.
    /// </summary>
    [Fact]
    public void The_card_is_solid_and_only_the_glow_around_it_is_see_through()
    {
        var xaml = Read("src", "DeskAI.App", "Views", "QuickSearchWindow.xaml");

        Assert.Contains("<views:SeeThroughBackdrop />", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"#FF0B1624\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("#F20B1624", xaml, StringComparison.Ordinal);
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

    /// <summary>
    /// Owner-found 2026-09-25: Windows' Animation effects was off on their PC, so no buddy ever
    /// moved. DeskAI's own "Let my buddy move" switch decides instead (the owner's choice).
    /// </summary>
    [Fact]
    public void Moves_follow_DeskAIs_own_switch_not_Windows()
    {
        var control = Read("src", "DeskAI.App", "Views", "Buddies", "BuddyControl.cs");

        Assert.DoesNotContain("AnimationsEnabled", control, StringComparison.Ordinal);
        Assert.DoesNotContain("UISettings", control, StringComparison.Ordinal);
        Assert.Contains("MotionSwitch is { IsOn: true }", control, StringComparison.Ordinal);
        Assert.Contains("\"Still\"", control, StringComparison.Ordinal);
        Assert.Contains("Header=\"Let my buddy move\"", Read("src", "DeskAI.App", "Views", "WorkspacePage.xaml"), StringComparison.Ordinal);
        Assert.Contains("MotionSwitch = welcome.Motion", Read("src", "DeskAI.App", "Views", "WelcomeDialog.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Owner-found 2026-09-25: the tiles clipped their Choose button and Paige's name pushed hers
    /// out of the card. The faces replace them: a named radio group, each face named and reporting
    /// whether it is chosen, the stage decorative, the name and line announced.
    /// </summary>
    [Fact]
    public void The_buddy_faces_are_a_named_radio_group_beside_a_decorative_stage()
    {
        var page = Read("src", "DeskAI.App", "Views", "WorkspacePage.xaml");
        var start = page.IndexOf("Text=\"Your search buddy\"", StringComparison.Ordinal);
        var end = page.IndexOf("Header=\"Let my buddy move\"", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "The buddy chooser sits between its label and the motion switch.");
        var workspace = page[start..end];

        Assert.Contains("<RadioButtons", workspace, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Search buddies\"", workspace, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{x:Bind ChooseName}\"", workspace, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BuddyStage\"", workspace, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("VariableSizedWrapGrid", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Choose\"", workspace, StringComparison.Ordinal);
    }

    /// <summary>
    /// Review finding 2026-09-25: the pop-in swapped in the half-size shape at once but started
    /// 0.12 s later, so a half-size buddy flashed over the search box on every opening.
    /// </summary>
    [Fact]
    public void The_buddy_stays_hidden_until_its_pop_in_starts()
    {
        var code = Read("src", "DeskAI.App", "Views", "Buddies", "BuddyAnimations.cs");

        Assert.Matches(new Regex(@"element\.Opacity = 0;[\s\S]*story\.Begin\(\);", RegexOptions.None, TimeSpan.FromSeconds(1)), code);
    }

    /// <summary>
    /// Owner-found 2026-09-26: buddies looked pixelated after popping in. The pop-in grew them from
    /// half size, and Windows drew them once at that size and kept stretching the small drawing
    /// (letting go of the grow at the end did not redraw them; checked in the UI preview). The
    /// entrance now only rises and fades, so the buddy is always drawn at full size.
    /// </summary>
    [Fact]
    public void The_buddys_entrance_never_changes_its_size()
    {
        var code = Read("src", "DeskAI.App", "Views", "Buddies", "BuddyAnimations.cs");
        var entrance = code[code.IndexOf("public static void PopIn", StringComparison.Ordinal)..code.IndexOf("internal static DoubleAnimation To", StringComparison.Ordinal)];

        Assert.Contains("new TranslateTransform { Y = 30 }", entrance, StringComparison.Ordinal);
        Assert.DoesNotContain("Scale", entrance, StringComparison.Ordinal);
    }

    /// <summary>
    /// Owner-found 2026-09-26: the example buttons under the box were ovals. A corner radius of 999
    /// on a 30 px button is squashed into an oval by Windows; they are now rounded like the bar's
    /// other buttons.
    /// </summary>
    [Fact]
    public void The_example_buttons_are_rounded_like_the_other_buttons()
    {
        var xaml = Read("src", "DeskAI.App", "Views", "QuickSearchWindow.xaml");

        Assert.DoesNotContain("CornerRadius=\"999\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnExampleClicked\" Content=\"{x:Bind}\" CornerRadius=\"8\"", xaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Owner-found 2026-09-26: rows could be chosen only with the keyboard. The whole row takes the
    /// pointer: over it selects the row, a click does what Enter does. A click on the row's own
    /// button is left to the button, so a file is never opened twice.
    /// </summary>
    [Fact]
    public void A_whole_row_answers_the_pointer_and_its_button_is_not_counted_twice()
    {
        var xaml = Read("src", "DeskAI.App", "Views", "QuickSearchWindow.xaml");
        var code = Read("src", "DeskAI.App", "Views", "QuickSearchWindow.xaml.cs");

        Assert.Contains("Background=\"Transparent\" PointerEntered=\"OnRowPointerEntered\" Tapped=\"OnRowTapped\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.PointAt(row)", code, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"OnRowTapped[\s\S]*is ButtonBase[\s\S]*ActivateAsync\(row, showInFolder: false\)", RegexOptions.None, TimeSpan.FromSeconds(1)), code);
    }

    /// <summary>
    /// Review finding 2026-09-25: seven faces in one row beside the stage need about 450 px; with
    /// DeskAI snapped to half a small screen the last faces were cut off and could not be clicked.
    /// Narrow windows get two rows of faces; wide ones keep the mockup's single row.
    /// </summary>
    [Fact]
    public void The_faces_wrap_into_two_rows_when_DeskAI_is_narrow()
    {
        var page = Read("src", "DeskAI.App", "Views", "WorkspacePage.xaml");

        Assert.Contains("MaxColumns=\"4\"", page, StringComparison.Ordinal);
        Assert.Contains("<AdaptiveTrigger MinWindowWidth=\"1200\" />", page, StringComparison.Ordinal);
        Assert.Contains("<Setter Target=\"BuddyFaces.MaxColumns\" Value=\"7\" />", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// Review finding 2026-09-25: a polite live setting alone is not announced; the stage raises
    /// the live-region event when the buddy changes, so Narrator reads the new name and line.
    /// </summary>
    [Fact]
    public void A_new_buddy_on_the_stage_is_announced()
    {
        var code = Read("src", "DeskAI.App", "Views", "WorkspacePage.xaml.cs");

        Assert.Contains("AutomationEvents.LiveRegionChanged", code, StringComparison.Ordinal);
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
