# "?" Help Pop-ups Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put a small "?" next to each feature in DeskAI that opens a short, plain explanation: what it is, what it does, and what it never does.

**Architecture:** All help text lives in one catalog in `DeskAI.Presentation` (no WinUI), so it is unit-tested for completeness, length, and jargon. A small `HelpButton` control in `DeskAI.App` looks a topic up by ID and shows it in a flyout styled by a XAML template in the theme. A test scans the page XAML so every button points at a real topic and every topic is placed somewhere.

**Tech Stack:** C# / .NET 10, WinUI 3 (Windows App SDK 2.4), xUnit v3.

**Spec:** `docs/superpowers/specs/2026-09-10-organize-your-own-folders-design.md` — Part 1, "'?' help, everywhere". This plan is build-order step 1.

## Global Constraints

- Each topic has three parts: "What it is" (one sentence), "What it does" (one or two sentences, with an example), "What it never does" (the relevant safety promise).
- Word limits: What it is ≤ 20 words; What it does ≤ 40 words; What it never does ≤ 25 words.
- No technical words in help text. Banned (whole words, any case): metadata, endpoint, provider, schema, sqlite, deterministic, authorization, authorize, telemetry, dto, api, index, token, json, http, llm, scope.
- The button is keyboard reachable, named "Help: <title>" for screen readers, with the tooltip "What is this?".
- The accent colour means "safe or confirmed" only (`DeskAITheme.xaml`). The "?" button is neutral; only the "What it never does" line carries the accent, because it is a safety promise.
- Organize is rebuilt in step 2, so in this step Organize gets only `organize.practice`. Step 2 adds the tidy topics.
- Help text must describe current behaviour truthfully. Nothing may imply a connected folder can be changed.
- Verification: `dotnet build DeskAI.sln -c Release --no-restore`, `dotnet test DeskAI.sln -c Release --no-build --no-restore`, `dotnet format DeskAI.sln --no-restore --verify-no-changes`.
- The owner asked for the frontend-design skill to be used on UI work: load `frontend-design:frontend-design` before Task 2's visual work.

---

### Task 1: The help catalog

**Files:**
- Create: `src/DeskAI.Presentation/Help/HelpTopic.cs`
- Create: `src/DeskAI.Presentation/Help/HelpCatalog.cs`
- Test: `tests/DeskAI.Presentation.Tests/HelpCatalogTests.cs`

**Interfaces:**
- Produces: `DeskAI.App.Help.HelpTopic(string Id, string Title, string WhatItIs, string WhatItDoes, string WhatItNeverDoes)`; `HelpTopic.Missing`; `HelpCatalog.All : IReadOnlyList<HelpTopic>`; `HelpCatalog.Find(string? id) : HelpTopic?`; `HelpCatalog.BannedWords : IReadOnlyList<string>`; constants `MaxWhatItIsWords = 20`, `MaxWhatItDoesWords = 40`, `MaxWhatItNeverDoesWords = 25`.

- [ ] **Step 1: Write the failing tests**

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/DeskAI.Presentation.Tests -c Release --no-restore`
Expected: FAIL to compile — `The type or namespace name 'Help' does not exist in the namespace 'DeskAI.App'`.

- [ ] **Step 3: Write `HelpTopic.cs`**

```csharp
namespace DeskAI.App.Help;

/// <summary>
/// One "?" explanation, in the three parts every explanation has.
/// </summary>
/// <remarks>
/// "What it never does" is its own part on purpose. The question a cautious person has about
/// a file tool is rarely "what does this do" and almost always "what could it do to my files",
/// so the answer to that gets its own line instead of being buried in a paragraph.
/// </remarks>
public sealed record HelpTopic(
    string Id,
    string Title,
    string WhatItIs,
    string WhatItDoes,
    string WhatItNeverDoes)
{
    /// <summary>Shown only if a page names a topic that does not exist; a test prevents that.</summary>
    public static HelpTopic Missing { get; } = new(
        "missing",
        "Help",
        "An explanation that has not been written yet.",
        "Nothing on this page changes your files unless it says so clearly.",
        "DeskAI never moves or deletes anything without you approving it.");
}
```

- [ ] **Step 4: Write `HelpCatalog.cs`**

```csharp
namespace DeskAI.App.Help;

/// <summary>
/// Every "?" explanation in DeskAI, in one place.
/// </summary>
/// <remarks>
/// <para>
/// Kept in one list rather than typed into each page so the rules for help text can be
/// tested: all three parts present, short enough to read at a glance, and free of the
/// technical words the main interface keeps out. Pages refer to a topic by its ID, and a test
/// scans the pages so a mistyped ID fails the build's tests instead of showing an empty pop-up.
/// </para>
/// <para>
/// Every sentence here must stay true. If a feature changes, its explanation changes in the
/// same commit; a help text that promises more than the app does is worse than none.
/// </para>
/// </remarks>
public static class HelpCatalog
{
    public const int MaxWhatItIsWords = 20;
    public const int MaxWhatItDoesWords = 40;
    public const int MaxWhatItNeverDoesWords = 25;

    /// <summary>Words the main interface keeps out, per the project's user-experience rules.</summary>
    public static IReadOnlyList<string> BannedWords { get; } =
    [
        "metadata", "endpoint", "provider", "schema", "sqlite", "deterministic", "authorization",
        "authorize", "telemetry", "dto", "api", "index", "token", "json", "http", "llm", "scope",
    ];

    public static IReadOnlyList<HelpTopic> All { get; } =
    [
        new("home.health", "Organization score",
            "A score out of 100 for how settled your connected folders look.",
            "It adds up two things shown below it: space that might be taken by copies, and files nobody has changed in about six months. Each part shows how much it counted.",
            "It never changes, moves, or deletes anything. It only describes."),
        new("home.duplicates", "Possible duplicates",
            "Files that are exactly the same size, so they might be copies of each other.",
            "DeskAI groups them so you can check them yourself. The same size is a hint, not proof: DeskAI has not compared what is inside them.",
            "It never deletes a copy. Nothing here changes your files."),
        new("home.storage", "Where your space is going",
            "A breakdown of how much space each kind of file uses in your connected folders.",
            "It shows totals for documents, pictures, videos, and more, plus your largest files, from what DeskAI remembered the last time it looked.",
            "It never opens, moves, or deletes files. Refresh a folder in Search to update it."),
        new("organize.practice", "Practice mode",
            "A safe place to try tidying with example files DeskAI makes up.",
            "You see suggested moves, tick the ones you want, and run them on the example files. Undo puts them back.",
            "It never touches your own files. The example files live in a temporary folder."),
        new("search.searching", "Searching",
            "A way to find files in the folders you connected by typing what you want.",
            "Type something like \"photos from last month\" or \"documents over 10 mb\". DeskAI shows how it read your words, then lists the files that match.",
            "Searching never moves, renames, or opens a file."),
        new("search.connect", "Connecting a folder",
            "Giving DeskAI permission to look at one folder you choose.",
            "DeskAI remembers the names, sizes, and dates of the files in it so you can search them. Disconnect it at any time and DeskAI forgets everything about it.",
            "Connecting never lets DeskAI move, rename, or delete anything."),
        new("search.readInside", "Reading inside files",
            "An extra permission, for one folder, to look at the words written inside text files.",
            "Then a search also finds notes and lists that contain your words, even when the file name does not. DeskAI reads only the start of plain text files.",
            "It never opens PDFs, Word files, or photos, and never saves or sends what it reads."),
        new("search.saved", "Saved searches",
            "A search you gave a name so you can run it again with one click.",
            "Running it searches again from scratch in the folders connected right now, so the results are always up to date.",
            "A saved search is not a folder. It never moves or copies files."),
        new("automation.checking", "Checking for you",
            "DeskAI looking at your connected folders by itself while the app is open.",
            "Every so often it checks whether any file matches your rules, and tells you if something does.",
            "It never moves a file on its own. It stops when you close DeskAI."),
        new("automation.frequency", "How often",
            "How often DeskAI looks at your connected folders while it is open.",
            "Pick every 15 minutes, every hour, a few times a day, or only when you press Check now.",
            "Looking more often never makes DeskAI change anything."),
        new("automation.pause", "Pause",
            "An off switch for automatic checks.",
            "Turn it on and DeskAI stops looking by itself straight away, even in the middle of a check.",
            "Pausing never deletes your rules or your history."),
        new("automation.notifications", "Windows notifications",
            "An optional message from Windows when a check finds something.",
            "It says how many files matched. It is off unless you turn it on; while it is off, DeskAI shows a quiet note inside the app instead.",
            "It never shows file names, so nobody looking at your screen sees them."),
        new("automation.rules", "Rules",
            "Instructions you write for a tidy-up you do again and again.",
            "For example: when a file name contains \"invoice\", it belongs in Sorted. You can turn a rule off or delete it at any time.",
            "Saving a rule never moves anything by itself."),
        new("automation.practice", "Practice run",
            "A preview of exactly what your rules would do right now.",
            "It lists each file that would move and where it would go, plus any files your rules disagree about, which are left alone.",
            "A practice run never moves anything."),
        new("automation.sentence", "Writing a rule in your own words",
            "A shortcut for filling in the rule form.",
            "Type something like \"move invoices to Documents\" and DeskAI fills in the boxes below. Check them, give the rule a name, and save it.",
            "It never saves a rule for you. It only fills in the boxes."),
        new("settings.sharing", "What online AI may see",
            "Your choice of what information about your files may be sent to online AI.",
            "Switch on only what you are happy to share, such as file types. It matters only if you turn on online AI below.",
            "Private and protected files are always left out. What is written inside your files is never sent."),
        new("settings.aiChoice", "Choosing how AI works",
            "Whether DeskAI uses AI at all, and where that AI runs.",
            "Choose no AI, AI running on this computer, or an online AI service with your own key. AI only ever gives ideas.",
            "If your choice stops working, DeskAI never quietly switches to a different service."),
        new("settings.key", "Your key",
            "The password-like code an online AI service gave you, so DeskAI can use your account.",
            "Windows keeps it safe. DeskAI uses it only for the service you picked, and only when you ask for AI ideas.",
            "DeskAI never shows your key again and never sends it to a different company."),
        new("settings.dailyLimit", "Daily limit",
            "The most online AI requests DeskAI may make in one day.",
            "Once it is reached, DeskAI stops asking the AI service until tomorrow. Each press of Get AI ideas counts as one request.",
            "DeskAI never retries by itself, so it cannot use up requests without you."),
        new("shell.scope", "What DeskAI can see",
            "A reminder of which of your folders DeskAI is allowed to look at right now.",
            "It counts the folders you connected and the files DeskAI remembers, and says if you let it read inside any of them.",
            "DeskAI never looks anywhere you have not chosen."),
    ];

    public static HelpTopic? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : All.FirstOrDefault(topic => string.Equals(topic.Id, id, StringComparison.Ordinal));
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet build tests/DeskAI.Presentation.Tests -c Release --no-restore; dotnet test tests/DeskAI.Presentation.Tests -c Release --no-build --no-restore --filter-class "*HelpCatalogTests"`
Expected: PASS. If a length or banned-word test fails, shorten or reword that topic's text; do not raise a limit or remove a banned word.

- [ ] **Step 6: Commit**

```powershell
git add src/DeskAI.Presentation/Help tests/DeskAI.Presentation.Tests/HelpCatalogTests.cs
git commit -m "feat(help): one tested catalog of plain explanations for every feature"
```

---

### Task 2: The "?" button, placed on every page

**Files:**
- Create: `src/DeskAI.App/Controls/HelpButton.cs`
- Modify: `src/DeskAI.App/Themes/DeskAITheme.xaml` (add `HelpButtonStyle` and `HelpTopicTemplate`)
- Modify: `src/DeskAI.App/Views/DashboardPage.xaml`, `OrganizePage.xaml`, `SearchPage.xaml`, `AutomationPage.xaml`, `SettingsPage.xaml`, `src/DeskAI.App/MainWindow.xaml`
- Test: `tests/DeskAI.Presentation.Tests/HelpPlacementTests.cs`
- Modify docs: `docs/TESTING.md` (coverage map row), `docs/MANUAL-TESTING.md` (checklist), `docs/UI-UX.md` (help pattern)

**Interfaces:**
- Consumes: `HelpCatalog.Find(string?)`, `HelpTopic.Missing` from Task 1.
- Produces: XAML element `<controls:HelpButton Topic="<id>" />` with `xmlns:controls="using:DeskAI.App.Controls"`.

- [ ] **Step 1: Write the failing placement tests**

```csharp
using System.Text.RegularExpressions;
using DeskAI.App.Help;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Reads the page files so the "?" buttons and the help catalog cannot drift apart: a typo
/// in a topic ID, or a topic nobody placed, fails here rather than on someone's screen.
/// </summary>
public sealed partial class HelpPlacementTests
{
    [Fact]
    public void Every_help_button_points_at_a_real_topic()
    {
        foreach (var (file, id) in PlacedTopics())
        {
            Assert.True(HelpCatalog.Find(id) is not null, $"{file} uses unknown help topic '{id}'.");
        }
    }

    [Fact]
    public void Every_topic_is_placed_on_a_page()
    {
        var placed = PlacedTopics().Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var topic in HelpCatalog.All)
        {
            Assert.True(placed.Contains(topic.Id), $"Help topic '{topic.Id}' is not on any page.");
        }
    }

    private static IEnumerable<(string File, string Id)> PlacedTopics()
    {
        var appFolder = Path.Combine(RepositoryRoot(), "src", "DeskAI.App");
        foreach (var file in Directory.EnumerateFiles(appFolder, "*.xaml", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                    && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            foreach (Match match in HelpButtonPattern().Matches(File.ReadAllText(file)))
            {
                yield return (Path.GetFileName(file), match.Groups[1].Value);
            }
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DeskAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("DeskAI.sln was not found above the test output.");
    }

    [GeneratedRegex(@"<controls:HelpButton[^>]*\bTopic=""([^""]+)""")]
    private static partial Regex HelpButtonPattern();
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet build tests/DeskAI.Presentation.Tests -c Release --no-restore; dotnet test tests/DeskAI.Presentation.Tests -c Release --no-build --no-restore --filter-class "*HelpPlacementTests"`
Expected: `Every_topic_is_placed_on_a_page` FAILS with "Help topic 'home.health' is not on any page." (`Every_help_button_points_at_a_real_topic` passes vacuously.)

- [ ] **Step 3: Load the frontend-design skill** and apply it to the button and pop-up look below, keeping the theme's rule that the accent means only "safe or confirmed".

- [ ] **Step 4: Add the style and pop-up template to `DeskAITheme.xaml`**

Add `xmlns:help="using:DeskAI.App.Help"` to the `ResourceDictionary` root, then append before `</ResourceDictionary>`:

```xml
    <!--
        The "?" button. Neutral on purpose: asking what something is confirms nothing, so it
        must not wear the accent that means "safe". Round and small so it reads as a hint
        attached to a title rather than a control that does something.
    -->
    <Style x:Key="HelpButtonStyle" TargetType="Button" BasedOn="{StaticResource DefaultButtonStyle}">
        <Setter Property="Width" Value="24" />
        <Setter Property="Height" Value="24" />
        <Setter Property="MinWidth" Value="0" />
        <Setter Property="MinHeight" Value="0" />
        <Setter Property="Padding" Value="0" />
        <Setter Property="CornerRadius" Value="12" />
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="Background" Value="Transparent" />
        <Setter Property="BorderBrush" Value="{ThemeResource DeskLineStrongBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="Foreground" Value="{ThemeResource DeskTextSecondaryBrush}" />
    </Style>

    <!--
        The pop-up. Three labelled parts, always in the same order, so after reading one a
        person knows where to look in every other. Only the last part carries the accent,
        because it is the one line that is a safety promise.
    -->
    <DataTemplate x:Key="HelpTopicTemplate">
        <StackPanel MaxWidth="320" Spacing="12">
            <TextBlock FontSize="15" FontWeight="SemiBold" TextWrapping="Wrap"
                       Foreground="{ThemeResource DeskTextPrimaryBrush}"
                       Text="{Binding Title}" />
            <StackPanel Spacing="3">
                <TextBlock Style="{StaticResource CaptionStyle}" Text="What it is" />
                <TextBlock Style="{StaticResource BodySecondaryStyle}"
                           Foreground="{ThemeResource DeskTextPrimaryBrush}"
                           Text="{Binding WhatItIs}" />
            </StackPanel>
            <StackPanel Spacing="3">
                <TextBlock Style="{StaticResource CaptionStyle}" Text="What it does" />
                <TextBlock Style="{StaticResource BodySecondaryStyle}"
                           Foreground="{ThemeResource DeskTextPrimaryBrush}"
                           Text="{Binding WhatItDoes}" />
            </StackPanel>
            <Border Padding="10,8" CornerRadius="0,4,4,0" BorderThickness="3,0,0,0"
                    Background="{ThemeResource DeskAccentSoftBrush}"
                    BorderBrush="{ThemeResource DeskAccentBrush}">
                <StackPanel Spacing="3">
                    <StackPanel Orientation="Horizontal" Spacing="6">
                        <FontIcon FontSize="12" Glyph="&#xE72E;"
                                  Foreground="{ThemeResource DeskAccentBrush}" />
                        <TextBlock FontSize="12" FontWeight="SemiBold"
                                   Foreground="{ThemeResource DeskAccentBrush}"
                                   Text="What it never does" />
                    </StackPanel>
                    <TextBlock Style="{StaticResource BodySecondaryStyle}"
                               Foreground="{ThemeResource DeskTextPrimaryBrush}"
                               Text="{Binding WhatItNeverDoes}" />
                </StackPanel>
            </Border>
        </StackPanel>
    </DataTemplate>
```

(`xmlns:help` is only needed if the template later switches to `x:DataType`; with `{Binding}` it may be omitted. Omit it if the XAML compiler warns that it is unused.)

- [ ] **Step 5: Write `HelpButton.cs`**

```csharp
using DeskAI.App.Help;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace DeskAI.App.Controls;

/// <summary>
/// The small "?" beside a feature. Pressing it shows that feature's explanation.
/// </summary>
/// <remarks>
/// The text comes from <see cref="HelpCatalog"/> by ID, never from the page, so every
/// explanation is held to the same tested rules. The button changes nothing; it only reads.
/// </remarks>
public sealed partial class HelpButton : Button
{
    public static readonly DependencyProperty TopicProperty = DependencyProperty.Register(
        nameof(Topic),
        typeof(string),
        typeof(HelpButton),
        new PropertyMetadata(null, (sender, _) => ((HelpButton)sender).DescribeForScreenReaders()));

    public HelpButton()
    {
        Style = (Style)Application.Current.Resources["HelpButtonStyle"];
        Content = new FontIcon { Glyph = "", FontSize = 11 };
        ToolTipService.SetToolTip(this, "What is this?");
        Click += OnClick;
    }

    /// <summary>The ID of a topic in <see cref="HelpCatalog"/>.</summary>
    public string? Topic
    {
        get => (string?)GetValue(TopicProperty);
        set => SetValue(TopicProperty, value);
    }

    private HelpTopic Resolve() => HelpCatalog.Find(Topic) ?? HelpTopic.Missing;

    private void DescribeForScreenReaders() =>
        AutomationProperties.SetName(this, $"Help: {Resolve().Title}");

    private void OnClick(object sender, RoutedEventArgs e)
    {
        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            Content = new ContentControl
            {
                Content = Resolve(),
                ContentTemplate = (DataTemplate)Application.Current.Resources["HelpTopicTemplate"],
                IsTabStop = false,
            },
        };
        flyout.ShowAt(this);
    }
}
```

- [ ] **Step 6: Place the buttons**

Add `xmlns:controls="using:DeskAI.App.Controls"` to the root element of each file below. For each title, wrap the existing `TextBlock` so the "?" sits right after it:

```xml
<StackPanel Orientation="Horizontal" Spacing="8">
    <!-- existing TextBlock unchanged -->
    <controls:HelpButton Topic="<id>" />
</StackPanel>
```

| File | Next to | Topic |
|---|---|---|
| `DashboardPage.xaml` | "How organized this looks" | `home.health` |
| `DashboardPage.xaml` | "Where your space is going" | `home.storage` |
| `DashboardPage.xaml` | "Possible duplicates" | `home.duplicates` |
| `OrganizePage.xaml` | the "Practice mode" label in the hero (add inside its existing horizontal `StackPanel`) | `organize.practice` |
| `SearchPage.xaml` | page title "Search" | `search.searching` |
| `SearchPage.xaml` | "Saved searches" | `search.saved` |
| `SearchPage.xaml` | "Folders DeskAI can search" | `search.connect` |
| `SearchPage.xaml` | "What search can and cannot do" | `search.readInside` |
| `SearchPage.xaml` | "Found inside your files" | `search.readInside` |
| `AutomationPage.xaml` | "Checking for you" | `automation.checking` |
| `AutomationPage.xaml` | "Check now" button: change the grid to `ColumnDefinitions="Auto,Auto,*"` and add `<controls:HelpButton Grid.Column="2" VerticalAlignment="Bottom" Margin="0,0,0,6" Topic="automation.frequency" />` | `automation.frequency` |
| `AutomationPage.xaml` | the pause `ToggleSwitch`: wrap it and the button in a horizontal `StackPanel`, button `VerticalAlignment="Top" Margin="0,2,0,0"` | `automation.pause` |
| `AutomationPage.xaml` | the notification `ToggleSwitch`, same wrapping | `automation.notifications` |
| `AutomationPage.xaml` | "Your rules" | `automation.rules` |
| `AutomationPage.xaml` | "Try a practice run": change the grid to `ColumnDefinitions="*,Auto,Auto"`, add `<controls:HelpButton Grid.Column="2" VerticalAlignment="Top" Margin="0,4,0,0" Topic="automation.practice" />` | `automation.practice` |
| `AutomationPage.xaml` | "Read my sentence": wrap the button and a help button in a horizontal `StackPanel` | `automation.sentence` |
| `SettingsPage.xaml` | "What online AI may see" | `settings.sharing` |
| `SettingsPage.xaml` | "Choose how AI works" | `settings.aiChoice` |
| `SettingsPage.xaml` | "Where your key is kept" | `settings.key` |
| `SettingsPage.xaml` | the daily-limit `NumberBox`: wrap with a horizontal `StackPanel`, button `VerticalAlignment="Bottom" Margin="0,0,0,6"` | `settings.dailyLimit` |
| `MainWindow.xaml` | `ScopeTitle` in the pane footer (inside its existing horizontal `StackPanel`) | `shell.scope` |

- [ ] **Step 7: Build and run all tests**

Run: `dotnet build DeskAI.sln -c Release --no-restore; dotnet test DeskAI.sln -c Release --no-build --no-restore; dotnet format DeskAI.sln --no-restore --verify-no-changes`
Expected: 0 warnings, 0 errors, all tests pass, format exit 0. The XAML compiler checks every `controls:HelpButton` element; `HelpPlacementTests` now pass.

- [ ] **Step 8: Update docs**

- `docs/TESTING.md` Feature Coverage Map: add `| Every page | "?" help next to each feature | HelpCatalogTests, HelpPlacementTests |`.
- `docs/MANUAL-TESTING.md`: add a "Help pop-ups" checklist — each "?" opens, shows three parts, closes with Esc; Tab reaches it; Narrator reads "Help: <title>"; readable in light, dark, and high contrast.
- `docs/UI-UX.md`: add a short "Help pop-ups" section describing the three-part pattern, the word limits, the banned words, and that help text changes in the same commit as the feature it describes.

- [ ] **Step 9: Commit**

```powershell
git add src/DeskAI.App tests/DeskAI.Presentation.Tests/HelpPlacementTests.cs docs
git commit -m "feat(help): a ? next to each feature that explains it in plain words"
```

---

### Final check

- [ ] Full build, tests, and format pass (commands in Global Constraints).
- [ ] Give the owner the exe path: `src\DeskAI.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\DeskAI.App.exe`, and say plainly what they will see: a "?" beside each feature on every page.
