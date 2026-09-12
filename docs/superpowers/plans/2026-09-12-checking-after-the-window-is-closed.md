# Checking After the Window Is Closed — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a person choose to have DeskAI keep running with no window, showing an icon near the clock, so its periodic check stays current after they close it.

**Architecture:** The window is hidden rather than closed, so the host and its check timer keep running untouched. A `IBackgroundPresence` interface in `DeskAI.Presentation` carries "show/hide the icon, set its tooltip, tell me when someone picks a menu item"; `DeskAI.App` implements it with `Shell_NotifyIcon` against a hidden ordinary top-level window. Every decision stays in Core and Presentation; the adapter holds none.

**Tech Stack:** C#, .NET 10, WinUI 3 / Windows App SDK (unpackaged, x64), SQLite, xUnit. No new package — `Shell_NotifyIcon` via P/Invoke.

**Spec:** `docs/decisions/0025-checking-after-the-window-is-closed.md` and `docs/security/2026-09-12-background-checking-review.md` (both committed at `e485ed3`). Read both before Task 1. Also `AGENTS.md`, and `docs/SECURITY.md` — which wins over everything here.

## Global Constraints

- **AI decides what it recommends; deterministic code decides what is allowed.** Nothing in this plan touches an executor, planner, scanner, content extractor, AI provider, or credential store.
- **DeskAI never registers itself to start with Windows.** No Run key, no Startup folder, no scheduled task, no `StartupTask`. Enforced by Task 8.
- **A control reachable with no window may stop DeskAI doing something; none may start it.** Menu is exactly: open, pause checking, quit.
- **A surface reachable with no window carries a count and a state, never a file name, folder name, or path.** Applies to the tray tooltip exactly as it already applies to notifications.
- **`DeskAI.Presentation` must never reference WinUI.** Presentation files use namespace `DeskAI.App.*` despite living in `src/DeskAI.Presentation/` — follow that existing convention.
- **Tests never touch real personal folders.** Generated files in temporary directories only (`TestApp`, `TemporaryDirectory`).
- **Anything a person can see or do needs a page test in `DeskAI.Presentation.Tests` and a row in the Feature Coverage Map in `docs/TESTING.md`.**
- Verification, run from the repository root:
  ```powershell
  dotnet build DeskAI.sln -c Release --no-restore
  dotnet test DeskAI.sln -c Release --no-build --no-restore
  dotnet format DeskAI.sln --no-restore --verify-no-changes
  ```
- Commit every completed task. End each commit message with:
  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01PkbanmsudK8GFe8oXbvuZK
  ```

## File Structure

**Create:**

| File | Responsibility |
|---|---|
| `src/DeskAI.Presentation/Services/IBackgroundPresence.cs` | The seam. Show/hide an icon, set its tooltip and pause state, raise Open/PauseToggled/Quit. Faked in page tests, exactly as `IFindingNotifier` is. |
| `src/DeskAI.Presentation/Services/BackgroundPresenceController.cs` | **Singleton.** The one owner of the tray wiring: subscribes to the presence, writes pause through the settings repository, stops a running check, keeps the icon and its tooltip matching what is stored. |
| `src/DeskAI.Core/Rules/BackgroundCheckingChoice.cs` | Pure: the dialog's words and the tooltip's words, derived from `AutomaticCheckSettings`. No UI, no I/O. |
| `src/DeskAI.App/Services/TrayPresence.cs` | `Shell_NotifyIcon` + hidden window + menu. The only untestable code; holds no decision. |
| `src/DeskAI.App/Services/SingleInstance.cs` | The mutex and the reveal message. |
| `src/DeskAI.Core/Rules/SingleInstanceDecision.cs` | Pure: "another one is running → reveal and exit". Unit-tested. |
| `tests/DeskAI.Presentation.Tests/BackgroundCheckingPageTests.cs` | The feature as a person uses it. |
| `tests/DeskAI.Core.Tests/BackgroundCheckingChoiceTests.cs` | The wording rules. |
| `tests/DeskAI.Core.Tests/SingleInstanceDecisionTests.cs` | The reveal-and-exit rule. |
| `tests/DeskAI.Core.Tests/NeverStartsWithWindowsTests.cs` | The solution-wide scan for startup-registration APIs. |

**Modify:** `src/DeskAI.Core/Rules/AutomaticCheckSettings.cs` (the "not buildable" remark), `src/DeskAI.Presentation/ViewModels/AutomationViewModel.cs` (the switch, the dialog, the wording), `src/DeskAI.Presentation/Help/HelpCatalog.cs` (one topic, and `automation.checking` stops promising it stops on close), `src/DeskAI.Presentation/Composition/` service registration, `src/DeskAI.App/Views/AutomationPage.xaml`, `src/DeskAI.App/App.xaml.cs`, `src/DeskAI.App/MainWindow.xaml.cs`, `docs/TESTING.md`, `docs/MANUAL-TESTING.md`, `docs/ROADMAP.md`, `docs/HANDOFF.md`.

**Task order rationale:** pure wording rules first (Tasks 1–2), then the seam and the view model the page tests drive (Tasks 3–5), then the Windows adapter that cannot be unit-tested (Tasks 6–7), then the containment tests that must pass over all of it (Task 8), then docs (Task 9). Every task before 6 is fully testable with no window.

---

### Task 1: The words, derived from the settings

The dialog text and the tray tooltip are the promises this feature makes. They are computed in Core from the stored settings so they cannot drift, for the same reason the navigation pane's scope label is computed rather than fixed.

**Files:**
- Create: `src/DeskAI.Core/Rules/BackgroundCheckingChoice.cs`
- Test: `tests/DeskAI.Core.Tests/BackgroundCheckingChoiceTests.cs`

**Interfaces:**
- Consumes: `AutomaticCheckSettings`, `AutomaticCheckMode`, `AutomaticCheckFrequency` from `DeskAI.Core.Rules`.
- Produces:
  - `sealed record BackgroundCheckingQuestion(string Title, string Body, string LimitLine, string NotifyLabel, string NotifyCaption, bool NotifyWhenSomethingIsFound, string Confirm, string Decline)`
  - `static class BackgroundCheckingChoice` with `static BackgroundCheckingQuestion Ask(AutomaticCheckSettings settings)`, `static string Tooltip(AutomaticCheckSettings settings, int? filesToReview)`, and `static string MoreDetails(AutomaticCheckMode mode)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/DeskAI.Core.Tests/BackgroundCheckingChoiceTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DeskAI.Core.Tests -c Release --filter BackgroundCheckingChoiceTests`
Expected: FAIL — `BackgroundCheckingChoice` and `BackgroundCheckingQuestion` do not exist.

- [ ] **Step 3: Write the implementation**

Create `src/DeskAI.Core/Rules/BackgroundCheckingChoice.cs`:

```csharp
namespace DeskAI.Core.Rules;

/// <summary>What someone is asked before DeskAI may keep running with no window.</summary>
/// <remarks>
/// A record rather than a dialog, so the words are testable without a window and the page
/// test can assert what a person was actually told. See ADR 0025.
/// </remarks>
public sealed record BackgroundCheckingQuestion(
    string Title,
    string Body,
    string LimitLine,
    string NotifyLabel,
    string NotifyCaption,
    bool NotifyWhenSomethingIsFound,
    string Confirm,
    string Decline);

/// <summary>
/// The words DeskAI uses about running with no window, derived from what is stored.
/// </summary>
/// <remarks>
/// <para>
/// Every sentence here is a promise about behaviour a person cannot see happening. It is
/// computed from the settings rather than written as a fixed string for the same reason the
/// navigation pane's scope label is: the one label that says what DeskAI is doing is the
/// label that must never be able to lie.
/// </para>
/// <para>
/// Pure by design. No I/O, no clock, no UI — so every promise in this feature can be
/// asserted in a unit test rather than read off a screen.
/// </para>
/// </remarks>
public static class BackgroundCheckingChoice
{
    /// <summary>The dialog shown before the mode is turned on. Asking, not announcing.</summary>
    public static BackgroundCheckingQuestion Ask(AutomaticCheckSettings settings) => new(
        Title: "Keep DeskAI running after you close the window?",
        Body: "DeskAI will stay near the clock and keep looking at the folders you connected. "
            + "It will not add itself to Windows startup — after you restart or sign out, it only "
            + "runs again when you open it.",
        LimitLine: "A check can tell you how many files your rules match. It cannot move, rename, "
            + "or delete anything. So leaving DeskAI on keeps that number up to date; it does not "
            + "tidy while you are away.",
        NotifyLabel: "Tell me with a Windows notification when something is found",
        NotifyCaption: "With this off, you'll see what it found the next time you open DeskAI.",
        NotifyWhenSomethingIsFound: settings.NotifyWhenSomethingIsFound,
        Confirm: "Keep running",
        Decline: "No thanks");

    /// <summary>
    /// What the icon near the clock says on hover.
    /// </summary>
    /// <remarks>
    /// A count and a state, never a file name, folder name, or path — the rule notifications
    /// already follow, for the same reason: this is shown to whoever is at the machine, which
    /// is not somewhere a person chose to show anyone their filenames.
    /// </remarks>
    public static string Tooltip(AutomaticCheckSettings settings, int? filesToReview)
    {
        if (settings.IsPaused)
        {
            // Deliberately before the count. A paused DeskAI is no longer keeping that
            // number current, so showing it would be showing something stale as if it were
            // being watched.
            return "DeskAI — checks paused";
        }

        if (settings.Frequency == AutomaticCheckFrequency.OnlyWhenIAsk)
        {
            return "DeskAI — only looks when you ask";
        }

        if (filesToReview is > 0 and var count)
        {
            return count == 1 ? "DeskAI — 1 file to review" : $"DeskAI — {count} files to review";
        }

        return settings.Frequency switch
        {
            AutomaticCheckFrequency.EveryFifteenMinutes => "DeskAI — looking every 15 minutes",
            AutomaticCheckFrequency.EveryHour => "DeskAI — looking every hour",
            _ => "DeskAI — looking a few times a day",
        };
    }

    /// <summary>
    /// The "More details" paragraph on the Automatic tasks page.
    /// </summary>
    /// <remarks>
    /// It used to be a fixed string opening "Checking happens only while DeskAI is open".
    /// That sentence is false in <see cref="AutomaticCheckMode.InBackground"/>, so the clause
    /// is derived. The startup sentence and the moves-nothing sentence stay in both, because
    /// they are true in both.
    /// </remarks>
    public static string MoreDetails(AutomaticCheckMode mode)
    {
        var opening = mode == AutomaticCheckMode.InBackground
            ? "Checking carries on after you close the window, until you quit DeskAI from the icon "
                + "near the clock, sign out, or restart."
            : "Checking happens only while DeskAI is open. Closing it stops everything.";

        return opening
            + " DeskAI does not add itself to Windows startup. A check re-reads the names, sizes, "
            + "and dates of files in the folders you connected — it does not open them, and it "
            + "does not move anything.";
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/DeskAI.Core.Tests -c Release --filter BackgroundCheckingChoiceTests`
Expected: PASS, 14 tests.

- [ ] **Step 5: Commit**

```bash
git add src/DeskAI.Core/Rules/BackgroundCheckingChoice.cs tests/DeskAI.Core.Tests/BackgroundCheckingChoiceTests.cs
git commit -F - <<'MSG'
feat(checks): the words for running with no window, computed rather than fixed

Every sentence about behaviour a person cannot watch is a promise, so it is
derived from the stored settings and asserted in a test. A paused DeskAI says
paused rather than showing a count it is no longer keeping current, and the
tooltip carries a count and a state but never a name.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01PkbanmsudK8GFe8oXbvuZK
MSG
```

---

### Task 2: The single-instance rule, as arithmetic

What a second launch does is a decision; holding a mutex is a mechanism. The decision is separated so it can be tested without starting two processes.

**Files:**
- Create: `src/DeskAI.Core/Rules/SingleInstanceDecision.cs`
- Test: `tests/DeskAI.Core.Tests/SingleInstanceDecisionTests.cs`

**Interfaces:**
- Produces:
  - `enum LaunchAction { StartNormally, RevealTheRunningOneAndExit }`
  - `static class SingleInstanceDecision` with `static LaunchAction Decide(bool anotherIsAlreadyRunning)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/DeskAI.Core.Tests/SingleInstanceDecisionTests.cs`:

```csharp
using DeskAI.Core.Rules;

namespace DeskAI.Core.Tests;

/// <summary>
/// What launching DeskAI a second time does. Never a second DeskAI, and never a silent
/// nothing: the one already running is revealed.
/// </summary>
public sealed class SingleInstanceDecisionTests
{
    [Fact]
    public void The_first_launch_starts_normally()
    {
        Assert.Equal(
            LaunchAction.StartNormally,
            SingleInstanceDecision.Decide(anotherIsAlreadyRunning: false));
    }

    [Fact]
    public void A_second_launch_reveals_the_one_already_running_and_exits()
    {
        Assert.Equal(
            LaunchAction.RevealTheRunningOneAndExit,
            SingleInstanceDecision.Decide(anotherIsAlreadyRunning: true));
    }

    [Fact]
    public void There_is_no_outcome_that_starts_a_second_DeskAI_or_quits_the_first()
    {
        // Two DeskAIs would mean two SQLite writers and two timers producing two counts for
        // one state. A launch that silently quit the first would lose what someone was doing.
        Assert.Equal(2, Enum.GetValues<LaunchAction>().Length);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DeskAI.Core.Tests -c Release --filter SingleInstanceDecisionTests`
Expected: FAIL — `LaunchAction` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/DeskAI.Core/Rules/SingleInstanceDecision.cs`:

```csharp
namespace DeskAI.Core.Rules;

/// <summary>What a launch of DeskAI should do.</summary>
public enum LaunchAction
{
    /// <summary>No other DeskAI is running in this sign-in session. Start.</summary>
    StartNormally = 0,

    /// <summary>
    /// One is already running, possibly with no window. Show it and exit successfully.
    /// </summary>
    RevealTheRunningOneAndExit = 1,
}

/// <summary>
/// Whether this launch is the real DeskAI or a request to reveal the one already running.
/// </summary>
/// <remarks>
/// <para>
/// Once DeskAI can run with no window, launching it again is the obvious thing a person does
/// when they want it back. Starting a second one would mean two SQLite writers against one
/// database and two timers producing two counts for one state; silently doing nothing would
/// look broken. So the running one is revealed and this launch ends.
/// </para>
/// <para>
/// Kept apart from the mutex that answers the question, so the rule is a unit test rather
/// than something only two real processes could demonstrate. See ADR 0025.
/// </para>
/// </remarks>
public static class SingleInstanceDecision
{
    public static LaunchAction Decide(bool anotherIsAlreadyRunning) => anotherIsAlreadyRunning
        ? LaunchAction.RevealTheRunningOneAndExit
        : LaunchAction.StartNormally;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/DeskAI.Core.Tests -c Release --filter SingleInstanceDecisionTests`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add src/DeskAI.Core/Rules/SingleInstanceDecision.cs tests/DeskAI.Core.Tests/SingleInstanceDecisionTests.cs
git commit -F - <<'MSG'
feat(checks): launching DeskAI again reveals the one already running

Separated from the mutex that answers the question so the rule is a unit test
rather than something only two real processes could demonstrate. Never a second
DeskAI — that would be two SQLite writers and two counts for one state — and
never a silent nothing.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01PkbanmsudK8GFe8oXbvuZK
MSG
```

---

### Task 3: The seam, and a fake for the page tests

**Files:**
- Create: `src/DeskAI.Presentation/Services/IBackgroundPresence.cs`
- Modify: `tests/DeskAI.Presentation.Tests/TestDoubles.cs` (add `RecordingPresence` at the end, beside `RecordingNotifier`)
- Modify: `tests/DeskAI.Presentation.Tests/TestApp.cs` (register the fake and expose it)

**Interfaces:**
- Produces:
  - `interface IBackgroundPresence` in namespace `DeskAI.App.Services` with: `bool IsShowing { get; }`, `void Show(string tooltip, bool isPaused)`, `void Update(string tooltip, bool isPaused)`, `void Hide()`, `event EventHandler? OpenRequested`, `event EventHandler? PauseToggleRequested`, `event EventHandler? QuitRequested`.
  - `RecordingPresence` test double with `List<string> Tooltips`, `List<bool> PausedStates`, `bool IsShowing`, and `void RaiseOpen()`, `void RaisePauseToggle()`, `void RaiseQuit()`.
  - `TestApp.Presence` returning `RecordingPresence`.

**Ruling R1 applies to this task.** The tooltip and the menu's pause checkmark travel together in one call, so the adapter cannot let them disagree — it is told what to display and displays it. There is no `SetPaused`.

- [ ] **Step 1: Write the interface**

Create `src/DeskAI.Presentation/Services/IBackgroundPresence.cs`. Note the namespace is `DeskAI.App.Services` — the existing convention for this project, matching `IFindingNotifier.cs` in the same folder.

```csharp
namespace DeskAI.App.Services;

/// <summary>
/// DeskAI's icon near the clock, while it is running with no window.
/// </summary>
/// <remarks>
/// <para>
/// Behind an interface because the notification area is a platform detail, and because a
/// presence that quietly does nothing is a legitimate implementation — the same reasoning as
/// <see cref="IFindingNotifier"/>. It also means everything a person can see or do here is a
/// page test rather than something only a screenshot could check.
/// </para>
/// <para>
/// It opens things and stops things. It starts nothing: there is deliberately no
/// "check now" here, because that would read folder metadata with no window on screen and no
/// page reporting the result. Every control that begins work stays on a page someone opened.
/// See ADR 0025.
/// </para>
/// <para>
/// The tooltip is handed in rather than composed here. What DeskAI may claim about itself is
/// decided in <see cref="DeskAI.Core.Rules.BackgroundCheckingChoice"/> and asserted there; an
/// implementation of this interface holds no such decision, and carries a count and a state
/// but never a file name, folder name, or path.
/// </para>
/// </remarks>
public interface IBackgroundPresence
{
    /// <summary>Whether the icon is on screen right now.</summary>
    bool IsShowing { get; }

    /// <summary>Shows the icon. Does nothing when it is already showing.</summary>
    void Show(string tooltip, bool isPaused);

    /// <summary>
    /// Changes what the icon says on hover, and whether its menu shows checking as paused.
    /// Does nothing when not showing.
    /// </summary>
    /// <remarks>
    /// The two travel together deliberately. An icon whose tooltip says it is looking every
    /// 15 minutes while its menu shows a tick beside "Pause checking" is the failure this
    /// whole feature is careful about, and separate calls are how that happens.
    /// </remarks>
    void Update(string tooltip, bool isPaused);

    /// <summary>Takes the icon away. Does nothing when it is not showing.</summary>
    void Hide();

    /// <summary>Someone asked for the DeskAI window back.</summary>
    event EventHandler? OpenRequested;

    /// <summary>Someone asked to pause or resume checking.</summary>
    event EventHandler? PauseToggleRequested;

    /// <summary>Someone asked DeskAI to stop altogether.</summary>
    event EventHandler? QuitRequested;
}
```

- [ ] **Step 2: Write the test double**

Append to `tests/DeskAI.Presentation.Tests/TestDoubles.cs`:

```csharp
/// <summary>The icon near the clock, as a test can see it.</summary>
internal sealed class RecordingPresence : IBackgroundPresence
{
    /// <summary>Every tooltip it has been given, in order. The last is what it says now.</summary>
    public List<string> Tooltips { get; } = [];

    /// <summary>Whether its menu showed checking as paused, alongside each tooltip.</summary>
    public List<bool> PausedStates { get; } = [];

    public bool IsShowing { get; private set; }

    public void Show(string tooltip, bool isPaused)
    {
        IsShowing = true;
        Tooltips.Add(tooltip);
        PausedStates.Add(isPaused);
    }

    public void Update(string tooltip, bool isPaused)
    {
        if (IsShowing)
        {
            Tooltips.Add(tooltip);
            PausedStates.Add(isPaused);
        }
    }

    public void Hide() => IsShowing = false;

    public event EventHandler? OpenRequested;

    public event EventHandler? PauseToggleRequested;

    public event EventHandler? QuitRequested;

    public void RaiseOpen() => OpenRequested?.Invoke(this, EventArgs.Empty);

    public void RaisePauseToggle() => PauseToggleRequested?.Invoke(this, EventArgs.Empty);

    public void RaiseQuit() => QuitRequested?.Invoke(this, EventArgs.Empty);
}
```

`TestDoubles.cs` already has `using DeskAI.App.Services;` for `IFindingNotifier`. Confirm it is present; add it if not.

- [ ] **Step 3: Register the fake in TestApp**

In `tests/DeskAI.Presentation.Tests/TestApp.cs`, beside the existing `Notifier` property add:

```csharp
    public RecordingPresence Presence => (RecordingPresence)_services.GetRequiredService<IBackgroundPresence>();
```

and beside the existing `Replace<IFindingNotifier>(...)` call add:

```csharp
        Replace<IBackgroundPresence>(services, new RecordingPresence());
```

- [ ] **Step 4: Register a real default so the app composes**

In `src/DeskAI.Presentation/Composition/` (the file holding `AddDeskAiApplication`, beside `services.AddTransient<AutomationViewModel>();`), register nothing for `IBackgroundPresence` — it is a Windows-facing service, so like `IFindingNotifier` it is registered in `App.xaml.cs` (Task 7). `TestApp.Replace` requires an existing registration, so instead add a do-nothing default in the shared registration:

```csharp
        // A DeskAI with no notification area is a legitimate DeskAI: it simply never offers
        // to keep running with no window. The Windows one is registered by the app.
        services.AddSingleton<IBackgroundPresence, NoBackgroundPresence>();
```

Create `src/DeskAI.Presentation/Services/NoBackgroundPresence.cs`:

```csharp
namespace DeskAI.App.Services;

/// <summary>A DeskAI with no icon near the clock. Everything else still works.</summary>
/// <remarks>
/// The default, so that a DeskAI composed without Windows-facing services is a working
/// DeskAI rather than a broken one. It never shows, so the Automatic tasks page never offers
/// to keep running with no window — see <c>CanKeepRunning</c> in the view model.
/// </remarks>
public sealed class NoBackgroundPresence : IBackgroundPresence
{
    public bool IsShowing => false;

    public void Show(string tooltip, bool isPaused)
    {
    }

    public void Update(string tooltip, bool isPaused)
    {
    }

    public void Hide()
    {
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? PauseToggleRequested;

    public event EventHandler? QuitRequested;
}
```

The three events are never raised here. Suppress or satisfy the "never used" warning the way the codebase already does elsewhere; if it produces a warning, add `#pragma warning disable CS0067` with a one-line comment explaining that a presence which never appears never raises anything.

- [ ] **Step 5: Write the controller — the one owner of the wiring**

This is ruling R1. Both view models are registered `AddTransient`, so subscribing either to a
singleton's event would add a handler per page visit and make one menu click toggle pause
several times. A singleton owner has exactly one subscription by construction.

Create `src/DeskAI.Presentation/Services/BackgroundPresenceController.cs`:

```csharp
using DeskAI.Core.Abstractions;
using DeskAI.Core.Rules;

namespace DeskAI.App.Services;

/// <summary>
/// The one thing that owns DeskAI's icon near the clock.
/// </summary>
/// <remarks>
/// <para>
/// A singleton on purpose. View models are created fresh for each page someone opens, so a
/// view model subscribing to the icon's events would add another handler every visit, and one
/// click on "Pause checking" would toggle it as many times as the page had been opened.
/// Having a single owner makes "the page and the icon never disagree" structural rather than
/// something each caller has to be careful about.
/// </para>
/// <para>
/// It decides nothing about what a check may do. It reads and writes the same settings the
/// Automatic tasks page reads and writes, stops a check that is running when someone pauses,
/// and hands the icon the words <see cref="BackgroundCheckingChoice"/> computed. See ADR 0025.
/// </para>
/// </remarks>
public sealed class BackgroundPresenceController : IDisposable
{
    private readonly IBackgroundPresence _presence;
    private readonly IAutomaticCheckSettingsRepository _settings;
    private readonly AutomaticCheckCoordinator _checks;
    private bool _disposed;

    public BackgroundPresenceController(
        IBackgroundPresence presence,
        IAutomaticCheckSettingsRepository settings,
        AutomaticCheckCoordinator checks)
    {
        _presence = presence;
        _settings = settings;
        _checks = checks;
        _presence.PauseToggleRequested += OnPauseToggleRequested;
        _checks.Checked += OnChecked;
    }

    /// <summary>Raised when something changed the settings from outside a page.</summary>
    /// <remarks>
    /// The Automatic tasks page listens so that pausing from the icon while the page is open
    /// is visible there immediately, rather than only after the page is opened again.
    /// </remarks>
    public event EventHandler? SettingsChangedOutsideThePage;

    /// <summary>Whether the icon is on screen.</summary>
    public bool IsShowing => _presence.IsShowing;

    /// <summary>
    /// Whether this DeskAI has a notification area to put an icon in at all.
    /// </summary>
    /// <remarks>
    /// False for <see cref="NoBackgroundPresence"/>. The page hides the switch rather than
    /// offering one that cannot work: an inert option still promises something.
    /// </remarks>
    public bool CanShowAnIcon => _presence is not NoBackgroundPresence;

    /// <summary>
    /// Makes the icon match what is stored: shown or not, and saying the right thing.
    /// </summary>
    /// <remarks>
    /// Called after every change rather than only when the mode changes, because the tooltip
    /// is one of the promises. An icon saying DeskAI is looking every 15 minutes while checks
    /// are paused is exactly the failure this feature has to avoid.
    /// </remarks>
    public void Refresh(AutomaticCheckSettings settings)
    {
        if (settings.Mode != AutomaticCheckMode.InBackground)
        {
            _presence.Hide();
            return;
        }

        var tooltip = BackgroundCheckingChoice.Tooltip(settings, _checks.Latest?.ProposalCount);
        if (_presence.IsShowing)
        {
            _presence.Update(tooltip, settings.IsPaused);
        }
        else
        {
            _presence.Show(tooltip, settings.IsPaused);
        }
    }

    /// <summary>
    /// Pause or resume, asked for from the icon rather than from a page.
    /// </summary>
    /// <remarks>
    /// Stopping is always safe, and it is the one control someone may want in a hurry. It
    /// goes through the same stored setting the page's switch uses, so the two cannot hold
    /// different answers, and it cancels a check already under way — someone reaching for a
    /// stop control means the thing happening now.
    /// </remarks>
    private void OnPauseToggleRequested(object? sender, EventArgs args) => _ = TogglePauseAsync();

    /// <summary>
    /// Pause or resume. Awaitable so a test can assert what happened rather than hope.
    /// </summary>
    public async Task TogglePauseAsync()
    {
        try
        {
            var stored = await _settings.LoadAsync().ConfigureAwait(false);
            var updated = stored with { IsPaused = !stored.IsPaused };
            if (updated.IsPaused)
            {
                _checks.StopRunningCheck();
            }

            await _settings.SaveAsync(updated).ConfigureAwait(false);
            Refresh(updated);
            SettingsChangedOutsideThePage?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.Data.Common.DbException
            or IOException
            or OperationCanceledException)
        {
            // There is no page on screen to show this on, and nothing was changed on disk.
            // The icon keeps saying what it said, which is still true.
        }
    }

    /// <summary>Keeps the count on the icon current after a check finishes.</summary>
    private async void OnChecked(object? sender, AutomaticCheckResult result)
    {
        try
        {
            Refresh(await _settings.LoadAsync().ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.Data.Common.DbException
            or IOException
            or OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _presence.PauseToggleRequested -= OnPauseToggleRequested;
        _checks.Checked -= OnChecked;
    }
}
```

Register it as a singleton in the shared composition, beside `IBackgroundPresence`:

```csharp
        services.AddSingleton<BackgroundPresenceController>();
```

- [ ] **Step 6: Build and run the full suite to confirm nothing regressed**

Run:
```powershell
dotnet build DeskAI.sln -c Release --no-restore
dotnet test DeskAI.sln -c Release --no-build --no-restore
```
Expected: build succeeds with 0 warnings; all existing tests still pass.

- [ ] **Step 7: Commit**

```bash
git add src/DeskAI.Presentation/Services/IBackgroundPresence.cs src/DeskAI.Presentation/Services/NoBackgroundPresence.cs src/DeskAI.Presentation/Services/BackgroundPresenceController.cs src/DeskAI.Presentation/Composition tests/DeskAI.Presentation.Tests/TestDoubles.cs tests/DeskAI.Presentation.Tests/TestApp.cs
git commit -F - <<'MSG'
feat(checks): a seam for the icon near the clock, so page tests can see it

Mirrors IFindingNotifier: a Windows surface behind an interface, with a
do-nothing default so a DeskAI composed without Windows-facing services is a
working DeskAI rather than a broken one.

It opens things and stops things and starts nothing. There is deliberately no
"check now" on it: that would read folder metadata with no window on screen and
no page reporting the result.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01PkbanmsudK8GFe8oXbvuZK
MSG
```

---

### Task 4: The switch and its dialog, on the Automatic tasks page

The view model gains the choice. The dialog is a record the page shows, and confirming is a separate method — the pattern `TidyViewModel` already uses for `TidyAiQuestion`, so consent is testable with no window.

**Files:**
- Modify: `src/DeskAI.Presentation/ViewModels/AutomationViewModel.cs`
- Modify: `src/DeskAI.Core/Rules/AutomaticCheckSettings.cs` (the `InBackground` remark is no longer true)
- Test: `tests/DeskAI.Presentation.Tests/BackgroundCheckingPageTests.cs` (create)

**Interfaces:**
- Consumes: `BackgroundCheckingChoice`, `BackgroundCheckingQuestion` (Task 1); `IBackgroundPresence` (Task 3).
- Produces, on `AutomationViewModel`:
  - `bool CanKeepRunning { get; }` — false when the presence can never show, so the switch is absent rather than present and inert.
  - `bool KeepsRunningWhenClosed { get; }` — the stored mode, as a switch reads it.
  - `BackgroundCheckingQuestion AskAboutKeepingRunning()` — the dialog's words. Stores nothing.
  - `Task KeepRunningAsync(bool notifyWhenSomethingIsFound)` — the person said yes.
  - `Task StopKeepingRunningAsync()` — the person turned it off, or said no having had it on.
  - `string MoreDetails { get; }` — derived from the mode.

- [ ] **Step 1: Write the failing page tests**

Create `tests/DeskAI.Presentation.Tests/BackgroundCheckingPageTests.cs`:

```csharp
using DeskAI.App.ViewModels;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Rules;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Keeping DeskAI running after the window is closed, as a person meets it: a switch, a
/// dialog that asks rather than announces, an icon near the clock, and a way back out.
/// </summary>
public sealed class BackgroundCheckingPageTests
{
    [Fact]
    public async Task It_is_off_until_someone_turns_it_on()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();

        await page.InitializeAsync();

        Assert.False(page.KeepsRunningWhenClosed);
        Assert.False(app.Presence.IsShowing);
        Assert.Equal(
            AutomaticCheckMode.WhileAppIsOpen,
            (await app.Get<IAutomaticCheckSettingsRepository>().LoadAsync(TestContext.Current.CancellationToken)).Mode);
    }

    [Fact]
    public async Task Turning_it_on_asks_first_and_stores_nothing_yet()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        var question = page.AskAboutKeepingRunning();

        Assert.Contains("Keep DeskAI running", question.Title, StringComparison.Ordinal);
        Assert.Contains("Windows startup", question.Body, StringComparison.Ordinal);
        Assert.False(page.KeepsRunningWhenClosed);
        Assert.False(app.Presence.IsShowing);
    }

    [Fact]
    public async Task The_question_says_a_check_cannot_move_anything()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        var question = page.AskAboutKeepingRunning();

        Assert.Contains("cannot move", question.LimitLine, StringComparison.Ordinal);
        Assert.Contains("does not tidy while you are away", question.LimitLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_question_carries_the_notification_switch_and_says_what_off_means()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        var question = page.AskAboutKeepingRunning();

        Assert.False(question.NotifyWhenSomethingIsFound);
        Assert.Contains("next time you open DeskAI", question.NotifyCaption, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Saying_no_changes_nothing()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        page.AskAboutKeepingRunning();
        // The person pressed "No thanks", so nothing is confirmed.

        Assert.False(page.KeepsRunningWhenClosed);
        Assert.False(app.Presence.IsShowing);
    }

    [Fact]
    public async Task Saying_yes_stores_it_and_shows_the_icon_while_the_window_is_still_open()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        Assert.True(page.KeepsRunningWhenClosed);
        Assert.True(app.Presence.IsShowing);
    }

    [Fact]
    public async Task Saying_yes_with_notifications_ticked_turns_them_on_too()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        await page.KeepRunningAsync(notifyWhenSomethingIsFound: true);

        Assert.True(page.NotifyWhenSomethingIsFound);
        var stored = await app.Get<IAutomaticCheckSettingsRepository>().LoadAsync(TestContext.Current.CancellationToken);
        Assert.True(stored.NotifyWhenSomethingIsFound);
    }

    [Fact]
    public async Task Saying_yes_without_ticking_notifications_leaves_them_off()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        Assert.False(page.NotifyWhenSomethingIsFound);
    }

    [Fact]
    public async Task The_choice_is_still_there_after_closing_DeskAI_and_opening_it_again()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        await using var reopened = await app.ReopenAsync();
        var again = reopened.Get<AutomationViewModel>();
        await again.InitializeAsync();

        Assert.True(again.KeepsRunningWhenClosed);
        Assert.True(reopened.Presence.IsShowing);
    }

    [Fact]
    public async Task Turning_it_off_takes_the_icon_away_at_once()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        await page.StopKeepingRunningAsync();

        Assert.False(page.KeepsRunningWhenClosed);
        Assert.False(app.Presence.IsShowing);
    }

    [Fact]
    public async Task While_it_is_on_the_page_does_not_claim_checking_stops_when_you_close_it()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        Assert.DoesNotContain("only while DeskAI is open", page.MoreDetails, StringComparison.Ordinal);
        Assert.DoesNotContain("While DeskAI is open", page.AutomaticCheckSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task It_never_stops_promising_that_nothing_moves_by_itself()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        Assert.Contains("never moves anything by itself", page.AutomaticCheckSummary, StringComparison.Ordinal);
        Assert.Contains("does not move anything", page.MoreDetails, StringComparison.Ordinal);
    }

    [Fact]
    public async Task It_promises_no_Windows_startup_whether_it_is_on_or_off()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        Assert.Contains("does not add itself to Windows startup", page.MoreDetails, StringComparison.Ordinal);

        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        Assert.Contains("does not add itself to Windows startup", page.MoreDetails, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_icon_says_how_often_it_is_looking()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        page.SelectedFrequency = page.FrequencyOptions.Single(
            option => option.Value == AutomaticCheckFrequency.EveryHour);

        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        Assert.Equal("DeskAI — looking every hour", app.Presence.Tooltips[^1]);
    }

    [Fact]
    public async Task Pausing_changes_what_the_icon_says()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        page.IsPaused = true;

        Assert.Equal("DeskAI — checks paused", app.Presence.Tooltips[^1]);
    }

    [Fact]
    public async Task The_icon_never_says_a_file_name()
    {
        await using var app = await TestApp.StartAsync();
        app.MakeFolder("Study", "invoice-april.txt");
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();

        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);
        page.SelectedFrequency = page.FrequencyOptions.Single(
            option => option.Value == AutomaticCheckFrequency.EveryFifteenMinutes);

        Assert.All(app.Presence.Tooltips, tooltip =>
        {
            Assert.DoesNotContain("invoice", tooltip, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Study", tooltip, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(app.Sandbox, tooltip, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Pausing_from_the_icon_pauses_on_the_page_too()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        await app.Get<BackgroundPresenceController>().TogglePauseAsync();

        Assert.True(page.IsPaused);
        Assert.Equal("DeskAI — checks paused", app.Presence.Tooltips[^1]);
        Assert.True(app.Presence.PausedStates[^1]);
    }

    [Fact]
    public async Task Pausing_from_the_icon_is_still_paused_after_reopening()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        await app.Get<BackgroundPresenceController>().TogglePauseAsync();

        await using var reopened = await app.ReopenAsync();
        var again = reopened.Get<AutomationViewModel>();
        await again.InitializeAsync();

        Assert.True(again.IsPaused);
    }

    [Fact]
    public async Task Resuming_from_the_icon_resumes()
    {
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);
        var controller = app.Get<BackgroundPresenceController>();
        await controller.TogglePauseAsync();

        await controller.TogglePauseAsync();

        Assert.False(page.IsPaused);
        Assert.False(app.Presence.PausedStates[^1]);
    }

    [Fact]
    public async Task The_menu_item_is_wired_to_the_same_thing_the_page_uses()
    {
        // Proves the event actually reaches the controller. The tests above call the method
        // directly so they can assert rather than race an async void handler; without this
        // one, a disconnected menu item would pass all of them.
        await using var app = await TestApp.StartAsync();
        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        await page.KeepRunningAsync(notifyWhenSomethingIsFound: false);

        app.Presence.RaisePauseToggle();

        // The handler is fire-and-forget by necessity — an event handler cannot be awaited.
        await WaitUntil(() => page.IsPaused);
        Assert.True(page.IsPaused);
    }

    /// <summary>Waits briefly for something a fire-and-forget handler will do.</summary>
    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Only_one_thing_listens_to_the_icon_however_many_times_the_page_is_opened()
    {
        // A transient view model subscribing to a singleton's event would toggle pause once
        // per page visit. Opening the page three times must still mean one toggle.
        await using var app = await TestApp.StartAsync();
        var first = app.Get<AutomationViewModel>();
        await first.InitializeAsync();
        await first.KeepRunningAsync(notifyWhenSomethingIsFound: false);
        foreach (var _ in Enumerable.Range(0, 3))
        {
            await app.Get<AutomationViewModel>().InitializeAsync();
        }

        await app.Get<BackgroundPresenceController>().TogglePauseAsync();

        var page = app.Get<AutomationViewModel>();
        await page.InitializeAsync();
        Assert.True(page.IsPaused);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DeskAI.Presentation.Tests -c Release --filter BackgroundCheckingPageTests`
Expected: FAIL — `KeepsRunningWhenClosed`, `AskAboutKeepingRunning`, `KeepRunningAsync`, `StopKeepingRunningAsync`, `MoreDetails`, and `TestApp.Presence` do not exist.

- [ ] **Step 3: Add the choice to the view model**

In `src/DeskAI.Presentation/ViewModels/AutomationViewModel.cs`:

1. Add `using DeskAI.App.Services;` if not already present.
2. Add `BackgroundPresenceController presence` as the last constructor parameter and store it as `_presence`. **Do not subscribe to `IBackgroundPresence` here** — that is ruling R1: this view model is transient, so a subscription per page visit would toggle pause several times on one menu click. Subscribe instead to the controller's own `SettingsChangedOutsideThePage`, and make the class `IDisposable` to unsubscribe, following `ShellViewModel`'s existing pattern.
3. Add a `_mode` field defaulting to `AutomaticCheckMode.WhileAppIsOpen`.
4. Add the members:

```csharp
    /// <summary>
    /// Whether this DeskAI can offer to keep running with no window at all.
    /// </summary>
    /// <remarks>
    /// False when there is no notification area to show an icon in. The switch is then
    /// absent rather than present and inert: an option that cannot work is worse than no
    /// option, because it promises something.
    /// </remarks>
    public bool CanKeepRunning => _presence.CanShowAnIcon;

    /// <summary>Whether DeskAI keeps checking after the window is closed.</summary>
    public bool KeepsRunningWhenClosed => _mode == AutomaticCheckMode.InBackground;

    /// <summary>The "More details" paragraph, which changes with the mode.</summary>
    public string MoreDetails => BackgroundCheckingChoice.MoreDetails(_mode);

    /// <summary>
    /// The words someone is shown before this is turned on. Stores nothing.
    /// </summary>
    /// <remarks>
    /// Asking is separated from doing so that a page test can assert what a person was
    /// actually told, and so that closing the dialog is genuinely a decision not to.
    /// </remarks>
    public BackgroundCheckingQuestion AskAboutKeepingRunning() =>
        BackgroundCheckingChoice.Ask(CurrentSettings());

    /// <summary>The person said yes, together with what they chose about notifications.</summary>
    public async Task KeepRunningAsync(bool notifyWhenSomethingIsFound)
    {
        _mode = AutomaticCheckMode.InBackground;
        _notifyWhenSomethingIsFound = notifyWhenSomethingIsFound;
        OnPropertyChanged(nameof(NotifyWhenSomethingIsFound));
        OnPropertyChanged(nameof(KeepsRunningWhenClosed));
        OnPropertyChanged(nameof(MoreDetails));
        OnPropertyChanged(nameof(AutomaticCheckSummary));
        await SaveCheckSettingsAsync().ConfigureAwait(true);
        RefreshPresence();
    }

    /// <summary>The person turned it off. The icon goes at once.</summary>
    public async Task StopKeepingRunningAsync()
    {
        _mode = AutomaticCheckMode.WhileAppIsOpen;
        OnPropertyChanged(nameof(KeepsRunningWhenClosed));
        OnPropertyChanged(nameof(MoreDetails));
        OnPropertyChanged(nameof(AutomaticCheckSummary));
        await SaveCheckSettingsAsync().ConfigureAwait(true);
        RefreshPresence();
    }

    private AutomaticCheckSettings CurrentSettings() => new(
        _mode,
        SelectedFrequency.Value,
        IsPaused,
        NotifyWhenSomethingIsFound);

    /// <summary>Hands the current settings to the one thing that owns the icon.</summary>
    private void RefreshPresence() => _presence.Refresh(CurrentSettings());

    /// <summary>
    /// Something outside this page changed the settings — pause, from the icon's menu.
    /// </summary>
    /// <remarks>
    /// Re-read rather than guessed at, so the page shows what is actually stored. The flag
    /// keeps this from counting as a fresh decision and writing the value straight back.
    /// </remarks>
    private async void OnSettingsChangedOutsideThePage(object? sender, EventArgs args)
    {
        try
        {
            var stored = await _checkSettings.LoadAsync().ConfigureAwait(true);
            _isApplyingStoredSettings = true;
            try
            {
                IsPaused = stored.IsPaused;
            }
            finally
            {
                _isApplyingStoredSettings = false;
            }
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI could not read that setting: {exception.Message}";
        }
    }
```

5. Change `SaveCheckSettings()` to store `_mode` instead of the hardcoded `AutomaticCheckMode.WhileAppIsOpen`, and extract an awaitable `SaveCheckSettingsAsync()` that the existing `async void SaveCheckSettings()` calls, so the new methods can await the write:

```csharp
    private async void SaveCheckSettings()
    {
        if (_isApplyingStoredSettings)
        {
            return;
        }

        await SaveCheckSettingsAsync().ConfigureAwait(true);
    }

    private async Task SaveCheckSettingsAsync()
    {
        try
        {
            await _checkSettings.SaveAsync(CurrentSettings()).ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI could not save that setting: {exception.Message}";
        }
    }
```

6. In `IsPaused`'s setter and `SelectedFrequency`'s setter, call `RefreshPresence()` after `SaveCheckSettings()` so the tooltip follows the state.
7. In `LoadCheckSettingsAsync`, set `_mode = stored.Mode;` inside the `_isApplyingStoredSettings` block, then after the block call `RefreshPresence();` and raise `OnPropertyChanged` for `KeepsRunningWhenClosed` and `MoreDetails`. This is what makes the choice survive a reopen.
8. Extend `AutomaticCheckSummary` with background wording. Each case still ends with the promise:

```csharp
    public string AutomaticCheckSummary => IsPaused
        ? "Automatic checks are paused. DeskAI is not looking at anything on its own."
        : (SelectedFrequency.Value, KeepsRunningWhenClosed) switch
        {
            (AutomaticCheckFrequency.OnlyWhenIAsk, _) =>
                "DeskAI only looks when you press Check now. It never moves anything by itself.",
            (AutomaticCheckFrequency.EveryFifteenMinutes, true) =>
                "DeskAI keeps looking every 15 minutes, even after you close the window, and "
                    + "tells you if your rules match anything. It never moves anything by itself.",
            (AutomaticCheckFrequency.EveryHour, true) =>
                "DeskAI keeps looking every hour, even after you close the window, and tells "
                    + "you if your rules match anything. It never moves anything by itself.",
            (_, true) =>
                "DeskAI keeps looking a few times a day, even after you close the window, and "
                    + "tells you if your rules match anything. It never moves anything by itself.",
            (AutomaticCheckFrequency.EveryFifteenMinutes, false) =>
                "While DeskAI is open it looks every 15 minutes and tells you if your rules "
                    + "match anything. It never moves anything by itself.",
            (AutomaticCheckFrequency.EveryHour, false) =>
                "While DeskAI is open it looks every hour and tells you if your rules match "
                    + "anything. It never moves anything by itself.",
            _ => "While DeskAI is open it looks a few times a day and tells you if your rules "
                + "match anything. It never moves anything by itself.",
        };
```

- [ ] **Step 4: Correct the comment that says this cannot be built**

In `src/DeskAI.Core/Rules/AutomaticCheckSettings.cs`, replace the `InBackground` summary:

```csharp
    /// <summary>
    /// DeskAI keeps checking after the window is closed, with an icon near the clock. It
    /// still adds nothing to Windows startup: after a restart or a sign-out it runs again
    /// only when someone opens it. Decided in ADR 0017, designed and reviewed in ADR 0025.
    /// </summary>
    InBackground = 1,
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/DeskAI.Presentation.Tests -c Release --filter BackgroundCheckingPageTests`
Expected: PASS, 19 tests.

Then the whole suite, because `AutomationViewModel`'s constructor changed:
```powershell
dotnet build DeskAI.sln -c Release --no-restore
dotnet test DeskAI.sln -c Release --no-build --no-restore
```
Expected: all pass. If `AutomationPageTests` fails on the summary sentence, check that the `WhileAppIsOpen` cases still read exactly as they did.

- [ ] **Step 6: Commit**

```bash
git add src/DeskAI.Presentation/ViewModels/AutomationViewModel.cs src/DeskAI.Core/Rules/AutomaticCheckSettings.cs tests/DeskAI.Presentation.Tests/BackgroundCheckingPageTests.cs
git commit -F - <<'MSG'
feat(checks): the choice to keep DeskAI running after the window is closed

Asking is separated from doing, as the AI dialog already is, so a page test can
assert what a person was actually told and so closing the dialog is genuinely a
decision not to. The icon appears when the setting is turned on rather than when
the window closes — while they are still looking at the switch that caused it.

Every sentence about the mode is derived from it. The page can no longer claim
checking stops when you close DeskAI while it does not, and no wording of the
summary ever stops saying that nothing moves by itself.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01PkbanmsudK8GFe8oXbvuZK
MSG
```

---

### Task 5: The switch on screen, and its help

**Files:**
- Modify: `src/DeskAI.App/Views/AutomationPage.xaml`
- Modify: `src/DeskAI.App/Views/AutomationPage.xaml.cs`
- Modify: `src/DeskAI.Presentation/Help/HelpCatalog.cs`

**Interfaces:**
- Consumes: `CanKeepRunning`, `KeepsRunningWhenClosed`, `AskAboutKeepingRunning()`, `KeepRunningAsync(bool)`, `StopKeepingRunningAsync()`, `MoreDetails` from Task 4.

- [ ] **Step 1: Add the help topic**

In `src/DeskAI.Presentation/Help/HelpCatalog.cs`, after the `automation.notifications` entry:

```csharp
        new("automation.keeprunning", "Keep running after you close it",
            "DeskAI staying near the clock and carrying on checking after you close its window.",
            "Right-click the icon to open DeskAI again, pause checking, or quit. It is off unless you turn it on.",
            "It never adds itself to Windows startup, and it still never moves a file on its own."),
```

Also correct `automation.checking`, whose last line promises something that is no longer always true:

```csharp
        new("automation.checking", "Checking for you",
            "DeskAI looking at your connected folders by itself.",
            "Every so often it checks whether any file matches your rules, and tells you if something does.",
            "It never moves a file on its own. It stops when you close DeskAI, unless you have asked it to keep running."),
```

- [ ] **Step 2: Add the switch to the page**

In `src/DeskAI.App/Views/AutomationPage.xaml`, after the notifications `StackPanel` and before the "More details" `Expander`:

```xml
                    <StackPanel Spacing="4" Visibility="{x:Bind ViewModel.CanKeepRunning}">
                        <StackPanel Orientation="Horizontal" Spacing="4">
                            <ToggleSwitch x:Name="KeepRunningSwitch"
                                          Header="Keep checking after I close the window"
                                          OnContent="On" OffContent="Off"
                                          IsOn="{x:Bind ViewModel.KeepsRunningWhenClosed, Mode=OneWay}"
                                          Toggled="OnKeepRunningToggled"
                                          AutomationProperties.Name="Keep checking after I close the window" />
                            <controls:HelpButton VerticalAlignment="Top" Topic="automation.keeprunning" />
                        </StackPanel>
                        <TextBlock Style="{StaticResource CaptionStyle}" MaxWidth="560"
                                   TextWrapping="Wrap"
                                   HorizontalAlignment="Left"
                                   Text="Off unless you turn it on. DeskAI will ask you first, and will never add itself to Windows startup." />
                    </StackPanel>
```

Note `Mode=OneWay` and a `Toggled` handler rather than a two-way binding: the switch must not store the choice by itself, because the answer to the dialog is what stores it.

Change the "More details" `TextBlock` to bind rather than hold a fixed string:

```xml
                        <TextBlock Style="{StaticResource BodySecondaryStyle}" MaxWidth="600"
                                   TextWrapping="Wrap"
                                   HorizontalAlignment="Left"
                                   Text="{x:Bind ViewModel.MoreDetails, Mode=OneWay}" />
```

- [ ] **Step 3: Show the dialog from the page**

In `src/DeskAI.App/Views/AutomationPage.xaml.cs`, add the handler. Follow the existing dialog code in `OrganizePage.xaml.cs` for `XamlRoot` and styling; the shape:

```csharp
    /// <summary>
    /// Turning this on asks before it does anything, because it changes what closing the
    /// window means. Turning it off needs no dialog: stopping is always safe.
    /// </summary>
    private async void OnKeepRunningToggled(object sender, RoutedEventArgs args)
    {
        if (ViewModel is null || KeepRunningSwitch.IsOn == ViewModel.KeepsRunningWhenClosed)
        {
            // The switch is only reflecting a change the view model already made.
            return;
        }

        if (!KeepRunningSwitch.IsOn)
        {
            await ViewModel.StopKeepingRunningAsync();
            return;
        }

        var question = ViewModel.AskAboutKeepingRunning();
        var notify = new CheckBox
        {
            Content = question.NotifyLabel,
            IsChecked = question.NotifyWhenSomethingIsFound,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = question.Title,
            PrimaryButtonText = question.Confirm,
            CloseButtonText = question.Decline,
            DefaultButton = ContentDialogButton.Close,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = question.Body, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = question.LimitLine, TextWrapping = TextWrapping.Wrap },
                    notify,
                    new TextBlock
                    {
                        Text = question.NotifyCaption,
                        TextWrapping = TextWrapping.Wrap,
                        Style = (Style)Application.Current.Resources["CaptionStyle"],
                    },
                },
            },
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.KeepRunningAsync(notify.IsChecked == true);
        }
        else
        {
            // Put the switch back where it was. Closing the dialog is a decision not to.
            KeepRunningSwitch.IsOn = false;
        }
    }
```

`DefaultButton = ContentDialogButton.Close` is deliberate: the narrow answer is the one a stray Enter gives.

- [ ] **Step 4: Build and run the help placement tests**

Run:
```powershell
dotnet build DeskAI.sln -c Release --no-restore
dotnet test DeskAI.sln -c Release --no-build --no-restore --filter HelpPlacementTests
```
Expected: PASS — `Every_topic_is_placed_on_a_page` proves the new topic is actually on the page, and `Every_help_button_points_at_a_real_topic` proves the ID is not a typo.

Then the full suite:
```powershell
dotnet test DeskAI.sln -c Release --no-build --no-restore
```

- [ ] **Step 5: Commit**

```bash
git add src/DeskAI.App/Views/AutomationPage.xaml src/DeskAI.App/Views/AutomationPage.xaml.cs src/DeskAI.Presentation/Help/HelpCatalog.cs
git commit -F - <<'MSG'
feat(checks): the switch on the page, and the dialog that asks first

The switch is one-way bound with a Toggled handler rather than two-way: the
answer to the dialog is what stores the choice, not the switch moving. Closing
the dialog puts it back. "No thanks" is the default button, so a stray Enter
gives the narrow answer.

"More details" is now bound rather than a fixed string, because its opening
sentence is false in the new mode. The checking help topic stopped promising
that closing DeskAI stops it.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01PkbanmsudK8GFe8oXbvuZK
MSG
```

---

### Task 6: The icon near the clock

The only code in this plan that cannot be unit-tested. It is therefore kept to exactly the part that cannot: a window, an icon, a menu, three events.

**Files:**
- Create: `src/DeskAI.App/Services/TrayPresence.cs`

**Interfaces:**
- Consumes: `IBackgroundPresence` (Task 3).
- Produces: `sealed partial class TrayPresence : IBackgroundPresence, IDisposable` with a constructor taking `ILogger<TrayPresence>`.

- [ ] **Step 1: Write the adapter**

Create `src/DeskAI.App/Services/TrayPresence.cs`. Key requirements, each of which is a control from the security review:

1. **The window is an ordinary top-level window that is never shown — not `HWND_MESSAGE`.** Create with `CreateWindowExW` using `WS_OVERLAPPED` and never call `ShowWindow`. `HWND_MESSAGE` windows do not receive broadcasts, and `TaskbarCreated` is a broadcast. Put this reason in a comment; it is the single most load-bearing line in the file.
2. **Re-add the icon on `TaskbarCreated`.** `RegisterWindowMessageW("TaskbarCreated")` at construction; on receiving it, re-add the icon if it was showing. Without this, an Explorer restart leaves DeskAI running, checking, and invisible.
3. **Remove the icon on `WM_QUERYENDSESSION`/`WM_ENDSESSION` and in `Dispose`.**
4. **The menu has exactly three items** — "Open DeskAI" (default, bold, also the left-click action), "Pause checking" (checkable), "Quit DeskAI". Build with `CreatePopupMenu`/`AppendMenuW`/`TrackPopupMenuEx`. Call `SetForegroundWindow` before `TrackPopupMenuEx` and post a null message after, or the menu will not dismiss when clicked away — a documented Win32 requirement.
5. **The tooltip is copied into the 128-char `szTip` field and truncated safely.** Task 1 keeps it under the limit; truncate defensively anyway rather than overrun.
6. **Every failure is contained.** `Shell_NotifyIcon` returning false is logged at Information and leaves `IsShowing` false — the same posture `WindowsFindingNotifier` takes. A missing icon must never take DeskAI down with it.
7. **The window class is registered as `DeskAI.TrayWindow`** — Task 7 looks it up by that exact string. **The pause checkmark comes from the `isPaused` parameter of `Show`/`Update`**; there is no `SetPaused` and the adapter stores no state of its own beyond what it was last told, so the tooltip and the checkmark cannot disagree.

Structure it as: the P/Invoke declarations in one `internal static partial class` region, the window class registration and `WndProc`, then the four interface members, then `Dispose`. Use `[LibraryImport]` source-generated P/Invoke to match the project's warning level. Keep the whole file under roughly 300 lines; if it grows past that, split the P/Invoke declarations into `src/DeskAI.App/Services/TrayInterop.cs`.

Write a class-level `<remarks>` that says plainly: this class holds no decision, the tooltip is handed to it, and the menu starts nothing.

- [ ] **Step 2: Build**

Run: `dotnet build DeskAI.sln -c Release --no-restore`
Expected: succeeds with 0 warnings. P/Invoke signature mistakes usually surface here as marshalling warnings — fix rather than suppress.

- [ ] **Step 3: Commit**

```bash
git add src/DeskAI.App/Services/TrayPresence.cs
git commit -F - <<'MSG'
feat(checks): the icon near the clock, via Shell_NotifyIcon and no new package

Its window is an ordinary top-level window that is never shown rather than a
message-only one, because TaskbarCreated is a broadcast and broadcasts do not
reach message-only windows. That one choice is what stops an Explorer restart
leaving DeskAI running, checking, and invisible.

The menu is open, pause, quit. It starts nothing. The tooltip is handed in
rather than composed here: what DeskAI may claim about itself is decided in
Core and asserted there.

A failure to show the icon is contained and logged, never fatal. A missing
icon is a missing convenience; taking DeskAI down with it would not be.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01PkbanmsudK8GFe8oXbvuZK
MSG
```

---

### Task 7: Hiding the window, quitting, and the second launch

**Files:**
- Create: `src/DeskAI.App/Services/SingleInstance.cs`
- Modify: `src/DeskAI.App/App.xaml.cs`
- Modify: `src/DeskAI.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `SingleInstanceDecision.Decide(bool)` and `LaunchAction` (Task 2); `TrayPresence` (Task 6); `IAutomaticCheckSettingsRepository` for the stored mode.
- Produces: `sealed class SingleInstance : IDisposable` with `static SingleInstance Acquire()`, `bool AnotherIsAlreadyRunning { get; }`, `void RevealTheRunningOne()`.

- [ ] **Step 1: Write the single-instance mechanism**

Create `src/DeskAI.App/Services/SingleInstance.cs`:

- A `Mutex` named `Local\DeskAI.SingleInstance`. **`Local\`, not `Global\`** — per sign-in session, so a second Windows user on the same machine gets their own DeskAI. Put that reason in a comment.
- `AnotherIsAlreadyRunning` is `!createdNew` from the mutex constructor.
- `RevealTheRunningOne()` finds the window by the window class name `DeskAI.TrayWindow` that `TrayPresence` registers and posts the message registered as `RegisterWindowMessageW("DeskAI.ShowExistingWindow")`. `PostMessage`, not `SendMessage`: a busy first instance must not hang the second.
- The message carries no `wParam` or `lParam`. Comment why: the most another program can achieve through this channel is causing a window to appear.
- An `AbandonedMutexException` on acquire means a previous DeskAI died holding it. Treat that as "no other DeskAI is running" and carry on.

- [ ] **Step 2: Wire it into startup**

In `App.xaml.cs`, in `OnLaunched`, before `_host.StartAsync()`:

```csharp
        _instance = SingleInstance.Acquire();
        if (SingleInstanceDecision.Decide(_instance.AnotherIsAlreadyRunning) == LaunchAction.RevealTheRunningOneAndExit)
        {
            // Never a second DeskAI: two SQLite writers against one database, and two timers
            // producing two counts for one state. Never a silent nothing either.
            _instance.RevealTheRunningOne();
            Exit();
            return;
        }
```

Register the Windows presence beside the notifier:

```csharp
                services.AddSingleton<IBackgroundPresence, TrayPresence>();
```

Ruling R2: this must REPLACE the shared `NoBackgroundPresence` registration, not sit alongside it — `TestApp` already carries the project's rule in a comment, that a second registration leaves the real one reachable through `IEnumerable<T>`. Remove the existing descriptor before adding, the way `TestApp.Replace` does. `TestApp` keeps its `RecordingPresence`.

- [ ] **Step 3: Hide instead of close, when the mode says so**

In `MainWindow.xaml.cs`, subscribe to `AppWindow.Closing` in the constructor:

```csharp
    /// <summary>
    /// Closing the window means hiding it when DeskAI has been asked to keep checking.
    /// </summary>
    /// <remarks>
    /// The host — and so the check timer — is untouched, which is the whole of the promise:
    /// the same DeskAI is still running. Reopening shows the state it was left in rather
    /// than a fresh start. With the mode off, this does nothing and the close is a real one.
    /// </remarks>
    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_keepsRunningWhenClosed)
        {
            args.Cancel = true;
            sender.Hide();
            ShowWhereItWentOnceEver();
        }
    }
```

`_keepsRunningWhenClosed` is read from `IAutomaticCheckSettingsRepository` when the window is created and refreshed on every navigation, alongside the existing `RefreshScopeAsync()` call — which already exists for exactly this reason, that the setting is changed on another page.

`ShowWhereItWentOnceEver()` shows the first-close notice once per install, via `IFindingNotifier` if available and otherwise not at all:

> **DeskAI is still running.** You'll find it near the clock. Right-click it to open DeskAI or quit.

Store the "already shown" flag through the existing settings repository rather than a new file.

- [ ] **Step 4: Wire the tray events**

In `App.xaml.cs`, after the window is created:

- `OpenRequested` → show and activate `_window`, and bring it to the front.
- `QuitRequested` → hide the icon, `await _host.StopAsync()`, `Exit()`.
- `PauseToggleRequested` needs no wiring here. `BackgroundPresenceController` (Task 3) owns it, and it is a singleton. **Resolve it once at startup** — `_host.Services.GetRequiredService<BackgroundPresenceController>();` right after the host starts — so its subscription exists before any page is opened. Without that line the controller is never constructed until someone visits the Automatic tasks page, and the icon's menu would do nothing on a DeskAI that was launched and closed without going there.
- Call `controller.Refresh(storedSettings)` once at startup too, so a DeskAI launched with the mode already on shows its icon before any page is opened.
- The registered reveal message arriving at the tray window → same as `OpenRequested`.
- Notification click (`AppNotificationManager.Default.NotificationInvoked`) → same as `OpenRequested`. A notification that does nothing when the window is hidden is a dead end.

- [ ] **Step 5: Build and run the full suite**

Run:
```powershell
dotnet build DeskAI.sln -c Release --no-restore
dotnet test DeskAI.sln -c Release --no-build --no-restore
```
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/DeskAI.App/Services/SingleInstance.cs src/DeskAI.App/App.xaml.cs src/DeskAI.App/MainWindow.xaml.cs
git commit -F - <<'MSG'
feat(checks): closing the window hides it, and launching again reveals it

Cancelling the close and hiding leaves the host and its timer untouched, which
is the whole promise: the same DeskAI is still running, and reopening shows the
state it was left in. With the mode off, the close is a real one.

The mutex is Local-scoped, so a second Windows user on the same machine gets
their own DeskAI. The reveal message carries no payload — the most another
program can achieve through that channel is causing a window to appear.

A notification now reveals the window when clicked. With the window hidden, one
that did nothing would be a dead end.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01PkbanmsudK8GFe8oXbvuZK
MSG
```

---

### Task 8: The containment tests

These are the tests the security review names. Until they pass, the review is not satisfied and the feature is not done.

**Files:**
- Create: `tests/DeskAI.Core.Tests/NeverStartsWithWindowsTests.cs`
- Modify: `tests/DeskAI.Core.Tests/AutomaticCheckServiceTests.cs` (extend the existing reflection test's reach)

- [ ] **Step 1: Write the startup-registration scan**

Create `tests/DeskAI.Core.Tests/NeverStartsWithWindowsTests.cs`:

```csharp
namespace DeskAI.Core.Tests;

/// <summary>
/// DeskAI never registers itself to start with Windows. This is a promise the app makes to
/// people in words, on the Automatic tasks page and in the dialog that turns background
/// checking on, so it is asserted rather than left to intent. See docs/SECURITY.md.
/// </summary>
public sealed class NeverStartsWithWindowsTests
{
    [Fact]
    public void No_source_file_registers_DeskAI_to_start_with_Windows()
    {
        string[] forbidden =
        [
            "CurrentVersion\\\\Run",
            "CurrentVersion/Run",
            "StartupTask",
            "Microsoft.Win32.Registry",
            "TaskScheduler",
            "schtasks",
            "SpecialFolder.Startup",
        ];

        var offenders = new List<string>();
        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var needle in forbidden)
            {
                if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{Path.GetFileName(file)} contains '{needle}'");
                }
            }
        }

        Assert.Empty(offenders);
    }

    private static IEnumerable<string> SourceFiles()
    {
        var source = Path.Combine(RepositoryRoot(), "src");
        var separator = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                           && !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal))
            .Concat(Directory.EnumerateFiles(source, "*.csproj", SearchOption.AllDirectories));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DeskAI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("DeskAI.sln not found.");
    }
}
```

Copy `RepositoryRoot()` from `HelpPlacementTests` if it already exists there in a shared form; do not duplicate it if a helper exists.

- [ ] **Step 2: Extend the containment test to the new code**

In `tests/DeskAI.Core.Tests/AutomaticCheckServiceTests.cs`, add beneath the existing test:

```csharp
    [Fact]
    public void Nothing_that_runs_with_no_window_can_reach_an_AI_or_a_credential()
    {
        var forbidden = new[]
        {
            typeof(IFolderTidyExecutor),
            typeof(IOperationJournal),
            typeof(IOrganizationPlanner),
            typeof(IFileScanner),
            typeof(IContentTextExtractor),
            typeof(ICredentialVault),
        };

        var dependencies = new[] { typeof(AutomaticCheckService), typeof(AutomaticCheckCoordinator) }
            .SelectMany(type => type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.All(forbidden, type => Assert.DoesNotContain(type, dependencies));

        // A provider interface must not be reachable either. Named rather than typed so this
        // fails even if a future AI contract is added that this test does not yet know about.
        Assert.DoesNotContain(dependencies, type => type.Name.Contains("Ai", StringComparison.Ordinal));
    }
```

Add `using DeskAI.Core.Abstractions;` for `ICredentialVault` if not present. If `ICredentialVault` lives elsewhere, adjust the using rather than dropping the assertion.

- [ ] **Step 3: Run them**

Run:
```powershell
dotnet test DeskAI.sln -c Release --no-build --no-restore --filter "NeverStartsWithWindowsTests|AutomaticCheckServiceTests"
```
Expected: PASS. If `No_source_file_registers_DeskAI_to_start_with_Windows` fails, do not weaken the test — remove whatever it found.

- [ ] **Step 4: Commit**

```bash
git add tests/DeskAI.Core.Tests/NeverStartsWithWindowsTests.cs tests/DeskAI.Core.Tests/AutomaticCheckServiceTests.cs
git commit -F - <<'MSG'
test(checks): the promises this mode makes, asserted rather than intended

DeskAI never registers itself to start with Windows — a scan over the source
rather than a convention, because it is a promise the app makes to people in
words. Nothing that runs with no window can reach an executor, a journal, a
planner, a scanner, a content extractor, a credential, or an AI.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01PkbanmsudK8GFe8oXbvuZK
MSG
```

---

### Task 9: Documentation, and closing V0.5

**Files:**
- Modify: `docs/TESTING.md` (Feature Coverage Map)
- Modify: `docs/MANUAL-TESTING.md`
- Modify: `docs/ROADMAP.md`
- Rewrite: `docs/HANDOFF.md`

- [ ] **Step 1: Add the coverage rows**

In `docs/TESTING.md`, after the existing `| Automatic tasks | Check now, history, how often, pause, notifications | AutomationPageTests |` row:

```markdown
| Automatic tasks | Keep checking after the window is closed: asked first, stores nothing until yes, survives reopening, off again at once | `BackgroundCheckingPageTests` |
| Automatic tasks | The icon near the clock: appears when turned on, says how often or paused, never a file name | `BackgroundCheckingPageTests`, `BackgroundCheckingChoiceTests` |
| Automatic tasks | Pause from the icon; the page and the icon never disagree | `BackgroundCheckingPageTests` |
| Automatic tasks | Wording follows the mode: never claims checking stops on close while it does not, always says nothing moves by itself, always says no Windows startup | `BackgroundCheckingPageTests`, `BackgroundCheckingChoiceTests` |
| Whole app | Never registers itself to start with Windows | `NeverStartsWithWindowsTests` |
| Whole app | Launching DeskAI again reveals the running one rather than starting a second | `SingleInstanceDecisionTests` |
```

- [ ] **Step 2: Add the owner's manual checks**

In `docs/MANUAL-TESTING.md`, add a section. These are the parts no automated test can reach, and each maps to a threat case in the review:

```markdown
## Checking after the window is closed (V0.5)

- [ ] Turn the switch on. The dialog appears and says it will not add itself to Windows startup. Press "No thanks" — the switch goes back and no icon appears.
- [ ] Turn it on and press "Keep running". The icon appears near the clock **while the window is still open**.
- [ ] Close the window. DeskAI stays in the notification area and the first-close notice tells you where it went.
- [ ] Hover the icon. It says how often DeskAI is looking. It never shows a file or folder name.
- [ ] Right-click the icon. Exactly three items: Open DeskAI, Pause checking, Quit DeskAI. There is no way to start anything.
- [ ] Pause from the icon, then open DeskAI. The page shows it as paused, and the tooltip says paused.
- [ ] Launch DeskAI again from the Start menu while it is hidden. The running window appears. There is no second DeskAI in Task Manager.
- [ ] Quit from the icon. The icon goes and `DeskAI.App.exe` is gone from Task Manager.
- [ ] With DeskAI hidden, restart Explorer (Task Manager → Windows Explorer → Restart). The icon comes back.
- [ ] Sign out and back in. DeskAI does **not** start on its own.
- [ ] Turn the switch off. The icon goes at once, and closing the window then really exits.
```

- [ ] **Step 3: Close the roadmap item**

In `docs/ROADMAP.md`, change the `◐ Folder watchers and/or scheduler selected through an ADR` bullet to `✅` and replace the trailing "NOT built" paragraph with what was actually built, naming ADR 0025 and the review. Then change the V0.5 heading from `(**Now**)` to `(**Complete — 2026-09-12**)` and mark the next version `(**Now**)` only if the owner says so — ask rather than assume.

- [ ] **Step 4: Rewrite the handoff**

Rewrite `docs/HANDOFF.md` following the "How to update this file" section at its end. It must record: V0.5 complete; what is left (the owner's manual sign-off list, now including the new rows from Step 2; V0.7 or V0.9 only if asked); and every decision the owner made in conversation that is not yet in code — the chat is cleared after each version.

Correct the launchable-app path while you are there. A solution-level Release build writes to:
`src\DeskAI.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\DeskAI.App.exe`

- [ ] **Step 5: Full verification**

Run all three, and paste the real output into the completion summary:
```powershell
dotnet build DeskAI.sln -c Release --no-restore
dotnet test DeskAI.sln -c Release --no-build --no-restore
dotnet format DeskAI.sln --no-restore --verify-no-changes
```

- [ ] **Step 6: Commit**

```bash
git add docs/TESTING.md docs/MANUAL-TESTING.md docs/ROADMAP.md docs/HANDOFF.md
git commit -F - <<'MSG'
docs: V0.5 complete — checking after the window is closed

Coverage map rows for the switch, the icon, pausing from it, and the wording
that follows the mode. Manual checks for the parts no test can reach: the
Explorer restart, the second launch, and signing out to prove DeskAI does not
come back.

Handoff rewritten for a cleared chat, with the launchable path corrected to the
one a solution-level Release build actually writes.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01PkbanmsudK8GFe8oXbvuZK
MSG
```

---

## Notes for whoever implements this

- **Rulings R1–R3 are already folded into the tasks below.** They came from a pre-flight scan of this plan against its spec, and are recorded in `.superpowers/sdd/2026-09-12-checking-after-the-window-is-closed/progress.md`. In short: the tray wiring lives in a singleton `BackgroundPresenceController`, not in either transient view model; `IBackgroundPresence` is registered once and replaced rather than added alongside; the tray window class name is `DeskAI.TrayWindow` and the pause state travels with the tooltip in one call.
- **Do not weaken a test to make a task pass.** If `NeverStartsWithWindowsTests` fails, the code is wrong, not the test. The same goes for the containment test.
- **The tooltip and dialog strings are the feature.** If you change one, change its test in the same commit and say why in the message.
- **Stop and ask** if implementation reveals that hiding the window does not keep the host alive as expected, or that `AppWindow.Closing` cannot be cancelled in this Windows App SDK version. Both are assumptions from the design; neither has been run.
