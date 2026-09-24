# Icon-Position Probe Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Find out, on a throwaway Windows Desktop, whether DeskAI can reliably place Desktop
icons, keep them placed, and put them back, before any Keep together / Make zones / Name the
zones code is written.

**Architecture:** A small console program, `IconPositionProbe`, that is **not part of the app**.
It talks to the Windows shell's Desktop view (`IShellWindows` → `IShellBrowser` → `IFolderView2`),
the approach described in Raymond Chen's "Manipulating the positions of desktop icons" (The Old
New Thing, 2013-11-18). It refuses to run anywhere except inside **Windows Sandbox**, whose
Desktop is created fresh and thrown away on close. A script publishes it, writes a `.wsb` file,
and starts the Sandbox with networking off. The probe writes a plain report to a results folder
under the repository's ignored `artifacts/`. The result decides whether step 2 goes ahead (ADR
0043) and is recorded in the ADR, the design, and the handoff.

**Tech Stack:** C# / .NET 10 console (`net10.0-windows`), classic `[ComImport]` COM interop and
`[DllImport]` (the repository does not enable unsafe code), xUnit v3 for the pure logic,
PowerShell for the Sandbox script.

**Spec:** `docs/superpowers/specs/2026-09-24-desktop-studio-design.md` ("Screen features" →
"Feasibility check first"; "Build order" step 2).

## Global Constraints

- The real Desktop is never touched by a test, by the probe, or by the agent (CLAUDE.md, spec
  "Adapter"). The probe's only allowed Desktop is Windows Sandbox's (`WDAGUtilityAccount`).
- "If it does not [work reliably], these three cards are dropped and the owner is told." (spec)
- No AI anywhere in this step. "AI plays no part here." (spec)
- Main screen only (spec).
- No registry API, no network: the `.wsb` sets `<Networking>Disable</Networking>`.
- Nothing from the probe ships in `DeskAI.App`; no app project references it.
- Target machine: the owner's Windows 11, build 10.0.26200 (the spec's "24H2" is corrected to
  the build actually installed).
- Every completed task is committed locally on `desktop-studio-find-groups`; nothing is pushed
  until the whole Desktop Studio feature is done (owner, 2026-09-24).
- Verification commands: `dotnet build DeskAI.sln -c Release --no-restore`,
  `dotnet test DeskAI.sln -c Release --no-build --no-restore`,
  `dotnet format DeskAI.sln --no-restore --verify-no-changes`.

## Why Windows Sandbox and not a generated folder

Since Windows 7, ordinary folder windows always auto-arrange; free icon positions exist only on
the Desktop. A generated folder in an Explorer window therefore cannot show whether positions
can be set and kept. Windows Sandbox gives a real, disposable Desktop on the same Windows build,
so the probe can change positions and Explorer settings with nothing personal in reach. The owner
has to turn the Sandbox on once (Windows Features → "Windows Sandbox", admin rights, one
restart); the agent does not do that.

## Review Focus

1. **Started on the owner's real PC** (double-clicked, or run by mistake) → it must refuse
   before creating a file or touching the shell, and say why. Pinned in Task 2 (`SandboxGuard`
   tests) and Task 3 (guard is the first line of `Main`).
2. **Desktop view not ready yet at logon** (the Sandbox runs the probe while Explorer is still
   starting) → wait up to 60 seconds for the view and its items, then report "not ready" rather
   than crash. Pinned in Task 3 (`WaitFor` with a timeout, tested with a fake clock in Task 2).
3. **Scaled screens** (the Sandbox window at 125–150 % scaling, a small work area) → targets must
   stay inside the work area; the report records the DPI, work area, and icon spacing. Pinned in
   Task 2 (`ProbeLayout` tests with a small work area and big spacing).
4. **Explorer restart never comes back or loses positions** → the probe waits a bounded time,
   records "lost" rather than failing silently, and still attempts Put back. Pinned in Task 3
   (each stage records its own outcome; Task 2 tests that a report with a lost stage says "not
   reliable").
5. **Two items with look-alike names** (extension hidden) → items are matched by their parsing
   name (with extension), and the probe's own files have unique generated names. Pinned in Task 2
   (`PositionCheck` matches by exact, case-insensitive parsing name).

---

## File Structure

| File | Responsibility |
|---|---|
| `docs/decisions/0043-desktop-icon-positions.md` | Proposed decision: how icons would be placed, the Sandbox-only probe, the go/no-go rule |
| `docs/security/2026-09-24-icon-position-probe-review.md` | Threat table for the probe itself |
| `tools/IconPositionProbe/IconPositionProbe.csproj` | Console project (in `DeskAI.sln`, solution folder `tools`) |
| `tools/IconPositionProbe/SandboxGuard.cs` | The only-in-Sandbox rule (pure) |
| `tools/IconPositionProbe/ProbeLayout.cs` | Target positions inside the work area (pure) |
| `tools/IconPositionProbe/PositionCheck.cs` | Compares expected and actual positions by name (pure) |
| `tools/IconPositionProbe/ProbeReport.cs` | Stage outcomes and the plain-text report and verdict (pure) |
| `tools/IconPositionProbe/Waiting.cs` | Bounded polling with an injectable clock (pure) |
| `tools/IconPositionProbe/DesktopShellView.cs` | COM interop: find the Desktop view, read/set positions and flags, refresh |
| `tools/IconPositionProbe/Program.cs` | The run: guard → make files → snapshot → place → refresh → restart Explorer → put back → report |
| `tools/IconPositionProbe/Run-InSandbox.ps1` | Publish, write the `.wsb`, start the Sandbox |
| `tests/DeskAI.IconProbe.Tests/*` | Tests for the pure parts |

---

### Task 1: Proposed decision and the probe's security review

**Files:**
- Create: `docs/decisions/0043-desktop-icon-positions.md`
- Create: `docs/security/2026-09-24-icon-position-probe-review.md`

**Interfaces:** none (documents only). Later tasks cite the test names listed in the review's
table; keep them identical.

- [ ] **Step 1: Write ADR 0043 with Status "Proposed"**

```markdown
# ADR 0043: Placing Desktop Icons (Proposed, waiting for the probe)

- Status: Proposed
- Date: 2026-09-24
- Review: `docs/security/2026-09-24-icon-position-probe-review.md`

## Context

Desktop Studio step 2 (Keep together, Make zones, Name the zones) needs DeskAI to place icons on
the Desktop. Windows has no documented setting for an icon's position. The shell's Desktop view
(`IFolderView2`, reached through `IShellWindows.FindWindowSW(SWC_DESKTOP)`) can read and set
positions when "Auto arrange icons" is off, and other tools use it. Whether that is reliable on
the owner's Windows (build 26200) — kept after a refresh and after Explorer restarts, and put back
exactly — is unknown.

## Decision (proposed)

- **Probe first.** `tools/IconPositionProbe` answers the question. It runs only inside Windows
  Sandbox (user `WDAGUtilityAccount`), refuses anywhere else, and works only on the Sandbox's
  throwaway Desktop with networking off. It is not part of the app.
- **Go** if, in the Sandbox: every placed icon reads back within half an icon-spacing step, the
  positions survive a view refresh, and Put back restores every original position and the Auto
  arrange setting. Surviving an Explorer restart is recorded; if it fails, the app design must
  re-apply or say so, and the owner decides.
- **No-go** otherwise: Keep together, Make zones, and Name the zones are dropped and the owner is
  told (design, "Feasibility check first").
- If go, a later plan adds a Core contract implemented with this same shell path, snapshot-first,
  asking before turning off Auto arrange, and Put back — with its own security review.

## Consequences

The probe changes Explorer settings and icon positions, but only on a Desktop that is deleted
when the Sandbox closes. No test, and no agent, touches the owner's Desktop.
```

- [ ] **Step 2: Write the probe's security review**

```markdown
# Icon-Position Probe Security Review

- Date: 2026-09-24
- Scope: ADR 0043's probe (`tools/IconPositionProbe`), not the later Desktop Studio cards
- Result: accepted for Windows Sandbox only

| Threat | Control | Test / evidence |
|---|---|---|
| The probe runs on the owner's real Desktop | `SandboxGuard.Check` runs first in `Main`; any user other than `WDAGUtilityAccount` exits with code 2 before a file or shell call | `SandboxGuardTests.Refuses_every_user_except_the_sandbox_account`, `SandboxGuardTests.Refuses_a_look_alike_name` |
| The probe changes something outside the Sandbox | The `.wsb` maps the probe folder read-only and only `artifacts/icon-probe/results` writable; networking is disabled | `Run-InSandbox.ps1` writes `<ReadOnly>true</ReadOnly>` and `<Networking>Disable</Networking>`; checked by reading the generated `.wsb` in Task 4 |
| The probe leaves the Desktop changed | Put back restores every original position and the Auto arrange flags; the Sandbox is discarded on close anyway | The report's `put back` stage; `ProbeReportTests.A_failed_put_back_makes_the_verdict_not_reliable` |
| Placed icons go off-screen | Targets are computed inside the work area | `ProbeLayoutTests.Targets_stay_inside_a_small_work_area` |
| Look-alike names confuse which icon moved | Matching by exact parsing name; generated unique names | `PositionCheckTests.Matches_by_full_name_not_by_display_name` |
| AI or a network service involved | None referenced; the project has no package or project references | `IconPositionProbe.csproj` has no `PackageReference`/`ProjectReference` |
| Personal data in the report | The report lists only the probe's own generated names and the Sandbox's default icons | Report written in the Sandbox; read by the owner before it is committed |

No registry API is used. Explorer is restarted only inside the Sandbox. The probe is never copied
into the app's output or release.
```

- [ ] **Step 3: Commit**

```bash
git add docs/decisions/0043-desktop-icon-positions.md docs/security/2026-09-24-icon-position-probe-review.md
git commit -m "Propose how Desktop icons would be placed, and review the Sandbox-only probe"
```

---

### Task 2: The probe project and its pure, tested parts

**Files:**
- Create: `tools/IconPositionProbe/IconPositionProbe.csproj`
- Create: `tools/IconPositionProbe/SandboxGuard.cs`, `ProbeLayout.cs`, `PositionCheck.cs`, `ProbeReport.cs`, `Waiting.cs`
- Create: `tools/IconPositionProbe/Program.cs` (temporary one-line stub so the exe builds; Task 3 replaces it)
- Create: `tests/DeskAI.IconProbe.Tests/DeskAI.IconProbe.Tests.csproj`, `GlobalUsings.cs`, `SandboxGuardTests.cs`, `ProbeLayoutTests.cs`, `PositionCheckTests.cs`, `ProbeReportTests.cs`, `WaitingTests.cs`
- Modify: `DeskAI.sln` (via `dotnet sln add`)

**Interfaces:**
- Produces (namespace `DeskAI.IconProbe`):
  - `readonly record struct Point(int X, int Y)`; `readonly record struct Rect(int Left, int Top, int Right, int Bottom)`
  - `static class SandboxGuard { const string SandboxUser = "WDAGUtilityAccount"; static string? Check(string userName); }` — null means allowed, otherwise the refusal text.
  - `static class ProbeLayout { static IReadOnlyList<Point> Targets(int count, Point spacing, Rect workArea); }`
  - `static class PositionCheck { static IReadOnlyList<string> Differences(IReadOnlyDictionary<string, Point> expected, IReadOnlyDictionary<string, Point> actual, Point spacing); }`
  - `enum StageOutcome { Passed, Failed, Skipped }`; `sealed record Stage(string Name, StageOutcome Outcome, string Detail)`
  - `sealed class ProbeReport { void Add(Stage stage); void Note(string line); bool IsReliable { get; } string Render(); }` — reliable when stages `place`, `refresh`, and `put back` all passed.
  - `static class Waiting { static async Task<T?> ForAsync<T>(Func<T?> probe, TimeSpan timeout, TimeSpan step, TimeProvider clock) where T : class; }`

- [ ] **Step 1: Create the projects and add them to the solution**

`tools/IconPositionProbe/IconPositionProbe.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <RootNamespace>DeskAI.IconProbe</RootNamespace>
    <!-- A development probe for ADR 0043. Never referenced by the app or shipped. -->
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="DeskAI.IconProbe.Tests" />
  </ItemGroup>
</Project>
```

`tools/IconPositionProbe/Program.cs` (stub, replaced in Task 3):

```csharp
return 0;
```

`tests/DeskAI.IconProbe.Tests/DeskAI.IconProbe.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\tools\IconPositionProbe\IconPositionProbe.csproj" />
  </ItemGroup>
</Project>
```

`tests/DeskAI.IconProbe.Tests/GlobalUsings.cs`:

```csharp
global using Xunit;
```

Run:

```powershell
dotnet sln DeskAI.sln add tools/IconPositionProbe/IconPositionProbe.csproj --solution-folder tools
dotnet sln DeskAI.sln add tests/DeskAI.IconProbe.Tests/DeskAI.IconProbe.Tests.csproj --solution-folder tests
dotnet restore DeskAI.sln
```

Expected: both projects added; `packages.lock.json` created for each (the repository uses lock
files). Commit the lock files with the task.

- [ ] **Step 2: Write the failing tests**

`tests/DeskAI.IconProbe.Tests/SandboxGuardTests.cs`:

```csharp
using DeskAI.IconProbe;

namespace DeskAI.IconProbe.Tests;

public sealed class SandboxGuardTests
{
    [Fact]
    public void Allows_only_the_sandbox_account() =>
        Assert.Null(SandboxGuard.Check("WDAGUtilityAccount"));

    [Theory]
    [InlineData("Hammouri")]
    [InlineData("Administrator")]
    [InlineData("")]
    public void Refuses_every_user_except_the_sandbox_account(string user) =>
        Assert.Contains("only inside Windows Sandbox", SandboxGuard.Check(user), StringComparison.Ordinal);

    [Theory]
    [InlineData("wdagutilityaccount")]
    [InlineData("WDAGUtilityAccount2")]
    [InlineData(" WDAGUtilityAccount")]
    public void Refuses_a_look_alike_name(string user) =>
        Assert.NotNull(SandboxGuard.Check(user));
}
```

`tests/DeskAI.IconProbe.Tests/ProbeLayoutTests.cs`:

```csharp
using DeskAI.IconProbe;

namespace DeskAI.IconProbe.Tests;

public sealed class ProbeLayoutTests
{
    [Fact]
    public void Twelve_targets_form_three_columns_of_four_one_spacing_apart()
    {
        var targets = ProbeLayout.Targets(12, new Point(75, 100), new Rect(0, 0, 1920, 1040));

        Assert.Equal(12, targets.Count);
        Assert.Equal(12, targets.Distinct().Count());
        Assert.Equal(3, targets.Select(t => t.X).Distinct().Count());
        Assert.Equal(4, targets.Select(t => t.Y).Distinct().Count());
        Assert.All(targets.Select(t => t.Y).Distinct().Order().Zip(targets.Select(t => t.Y).Distinct().Order().Skip(1)),
            pair => Assert.Equal(100, pair.Second - pair.First));
    }

    [Fact]
    public void Targets_sit_away_from_the_top_left_where_Windows_puts_new_icons()
    {
        var targets = ProbeLayout.Targets(12, new Point(75, 100), new Rect(0, 0, 1920, 1040));

        Assert.All(targets, t => Assert.True(t.X >= 1920 / 2, $"{t} is on the left half"));
    }

    [Fact]
    public void Targets_stay_inside_a_small_work_area()
    {
        // A 1280 x 720 Sandbox window at 150 % scaling: big spacing, little room.
        var area = new Rect(0, 0, 1280, 720);
        var targets = ProbeLayout.Targets(12, new Point(110, 130), area);

        Assert.All(targets, t =>
        {
            Assert.InRange(t.X, area.Left, area.Right - 110);
            Assert.InRange(t.Y, area.Top, area.Bottom - 130);
        });
    }

    [Fact]
    public void Refuses_when_the_icons_cannot_fit() =>
        Assert.Throws<InvalidOperationException>(() => ProbeLayout.Targets(12, new Point(300, 300), new Rect(0, 0, 600, 600)));
}
```

`tests/DeskAI.IconProbe.Tests/PositionCheckTests.cs`:

```csharp
using DeskAI.IconProbe;

namespace DeskAI.IconProbe.Tests;

public sealed class PositionCheckTests
{
    private static readonly Point Spacing = new(76, 100);

    [Fact]
    public void Positions_within_half_a_step_match()
    {
        var expected = new Dictionary<string, Point> { ["probe-01.txt"] = new(1000, 200) };
        var actual = new Dictionary<string, Point> { ["probe-01.txt"] = new(1037, 249) };

        Assert.Empty(PositionCheck.Differences(expected, actual, Spacing));
    }

    [Fact]
    public void A_moved_or_missing_icon_is_named()
    {
        var expected = new Dictionary<string, Point> { ["probe-01.txt"] = new(1000, 200), ["probe-02.txt"] = new(1000, 300) };
        var actual = new Dictionary<string, Point> { ["probe-01.txt"] = new(20, 20) };

        var differences = PositionCheck.Differences(expected, actual, Spacing);

        Assert.Equal(2, differences.Count);
        Assert.Contains(differences, d => d.Contains("probe-01.txt", StringComparison.Ordinal) && d.Contains("(20, 20)", StringComparison.Ordinal));
        Assert.Contains(differences, d => d.Contains("probe-02.txt", StringComparison.Ordinal) && d.Contains("missing", StringComparison.Ordinal));
    }

    [Fact]
    public void Matches_by_full_name_not_by_display_name()
    {
        var expected = new Dictionary<string, Point> { ["probe-01.txt"] = new(1000, 200) };

        // Windows ignores case, so a different case is the same icon...
        Assert.Empty(PositionCheck.Differences(expected, new Dictionary<string, Point> { ["PROBE-01.TXT"] = new(1000, 200) }, Spacing));
        // ...but a name without its extension is a different icon, reported as missing.
        Assert.Single(PositionCheck.Differences(expected, new Dictionary<string, Point> { ["probe-01"] = new(1000, 200) }, Spacing));
    }
}
```

`tests/DeskAI.IconProbe.Tests/ProbeReportTests.cs`:

```csharp
using DeskAI.IconProbe;

namespace DeskAI.IconProbe.Tests;

public sealed class ProbeReportTests
{
    [Fact]
    public void Reliable_when_place_refresh_and_put_back_pass_even_if_restart_loses_positions()
    {
        var report = Passing();
        report.Add(new Stage("explorer restart", StageOutcome.Failed, "3 icons moved"));

        Assert.True(report.IsReliable);
        Assert.Contains("VERDICT: reliable", report.Render(), StringComparison.Ordinal);
        Assert.Contains("explorer restart: Failed - 3 icons moved", report.Render(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_put_back_makes_the_verdict_not_reliable()
    {
        var report = new ProbeReport();
        report.Add(new Stage("place", StageOutcome.Passed, ""));
        report.Add(new Stage("refresh", StageOutcome.Passed, ""));
        report.Add(new Stage("put back", StageOutcome.Failed, "auto arrange still off"));

        Assert.False(report.IsReliable);
        Assert.Contains("VERDICT: not reliable", report.Render(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_stage_is_not_reliable()
    {
        var report = new ProbeReport();
        report.Add(new Stage("place", StageOutcome.Passed, ""));

        Assert.False(report.IsReliable);
    }

    [Fact]
    public void Notes_appear_in_the_report()
    {
        var report = Passing();
        report.Note("dpi: 144");

        Assert.Contains("dpi: 144", report.Render(), StringComparison.Ordinal);
    }

    private static ProbeReport Passing()
    {
        var report = new ProbeReport();
        report.Add(new Stage("place", StageOutcome.Passed, ""));
        report.Add(new Stage("refresh", StageOutcome.Passed, ""));
        report.Add(new Stage("put back", StageOutcome.Passed, ""));
        return report;
    }
}
```

`tests/DeskAI.IconProbe.Tests/WaitingTests.cs` (uses `Microsoft.Extensions.Time.Testing` only if
already available; otherwise the small fake below):

```csharp
using DeskAI.IconProbe;

namespace DeskAI.IconProbe.Tests;

public sealed class WaitingTests
{
    [Fact]
    public async Task Returns_the_value_as_soon_as_it_appears()
    {
        var calls = 0;
        var result = await Waiting.ForAsync(() => ++calls == 3 ? "ready" : null, TimeSpan.FromSeconds(60), TimeSpan.Zero, TimeProvider.System);

        Assert.Equal("ready", result);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Gives_up_with_null_after_the_timeout()
    {
        var clock = new SteppingClock(TimeSpan.FromSeconds(10));

        var result = await Waiting.ForAsync<string>(() => null, TimeSpan.FromSeconds(60), TimeSpan.Zero, clock);

        Assert.Null(result);
        Assert.InRange(clock.Reads, 6, 9);
    }

    /// <summary>Each read of the time moves it forward, so a wait ends without sleeping.</summary>
    private sealed class SteppingClock(TimeSpan step) : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;

        public int Reads { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            _now += step;
            return _now;
        }
    }
}
```

- [ ] **Step 3: Run the tests to see them fail**

Run: `dotnet build tests/DeskAI.IconProbe.Tests -c Release --no-restore`
Expected: build errors — `SandboxGuard`, `ProbeLayout`, `PositionCheck`, `ProbeReport`, `Waiting`, `Point`, `Rect` do not exist.

- [ ] **Step 4: Write the pure parts**

`tools/IconPositionProbe/SandboxGuard.cs`:

```csharp
namespace DeskAI.IconProbe;

internal readonly record struct Point(int X, int Y);

internal readonly record struct Rect(int Left, int Top, int Right, int Bottom);

/// <summary>
/// The probe changes Explorer settings and icon positions, so it may run only on Windows
/// Sandbox's throwaway Desktop. The Sandbox always signs in as this exact account.
/// </summary>
internal static class SandboxGuard
{
    internal const string SandboxUser = "WDAGUtilityAccount";

    /// <returns>Null when the probe may run; otherwise why it refuses.</returns>
    internal static string? Check(string userName) =>
        string.Equals(userName, SandboxUser, StringComparison.Ordinal)
            ? null
            : "This probe runs only inside Windows Sandbox, because it moves Desktop icons. Nothing was changed.";
}
```

`tools/IconPositionProbe/ProbeLayout.cs`:

```csharp
namespace DeskAI.IconProbe;

/// <summary>Where the probe puts its icons: columns of four on the right half of the work area.</summary>
/// <remarks>Windows drops new icons at the top left, so targets on the right half cannot pass by accident.</remarks>
internal static class ProbeLayout
{
    private const int PerColumn = 4;

    internal static IReadOnlyList<Point> Targets(int count, Point spacing, Rect workArea)
    {
        var columns = (count + PerColumn - 1) / PerColumn;
        var width = columns * spacing.X;
        var height = Math.Min(count, PerColumn) * spacing.Y;
        var left = workArea.Right - width - spacing.X;
        var top = workArea.Top + spacing.Y;
        var middle = workArea.Left + ((workArea.Right - workArea.Left) / 2);
        if (left < middle || top + height > workArea.Bottom)
        {
            throw new InvalidOperationException("The probe's icons do not fit on the right half of this screen.");
        }

        return Enumerable.Range(0, count)
            .Select(i => new Point(left + (i / PerColumn * spacing.X), top + (i % PerColumn * spacing.Y)))
            .ToList();
    }
}
```

`tools/IconPositionProbe/PositionCheck.cs`:

```csharp
namespace DeskAI.IconProbe;

internal static class PositionCheck
{
    /// <summary>
    /// Every expected icon that is missing or more than half a spacing step away. Names are
    /// parsing names (with extensions), compared ignoring case as Windows does.
    /// </summary>
    internal static IReadOnlyList<string> Differences(
        IReadOnlyDictionary<string, Point> expected, IReadOnlyDictionary<string, Point> actual, Point spacing)
    {
        var byName = actual.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var differences = new List<string>();
        foreach (var (name, want) in expected)
        {
            if (!byName.TryGetValue(name, out var got))
            {
                differences.Add($"{name}: missing");
            }
            else if (Math.Abs(got.X - want.X) > spacing.X / 2 || Math.Abs(got.Y - want.Y) > spacing.Y / 2)
            {
                differences.Add($"{name}: wanted ({want.X}, {want.Y}), found ({got.X}, {got.Y})");
            }
        }

        return differences;
    }
}
```

`tools/IconPositionProbe/ProbeReport.cs`:

```csharp
using System.Text;

namespace DeskAI.IconProbe;

internal enum StageOutcome
{
    Passed,
    Failed,
    Skipped,
}

internal sealed record Stage(string Name, StageOutcome Outcome, string Detail);

/// <summary>The probe's findings as plain text, and the ADR 0043 go/no-go verdict.</summary>
internal sealed class ProbeReport
{
    /// <summary>ADR 0043: these must pass. "explorer restart" is recorded but decided by the owner.</summary>
    private static readonly string[] Required = ["place", "refresh", "put back"];

    private readonly List<Stage> _stages = [];
    private readonly List<string> _notes = [];

    internal void Add(Stage stage) => _stages.Add(stage);

    internal void Note(string line) => _notes.Add(line);

    internal bool IsReliable => Required.All(name => _stages.Any(s => s.Name == name && s.Outcome == StageOutcome.Passed));

    internal string Render()
    {
        var text = new StringBuilder();
        text.AppendLine(IsReliable ? "VERDICT: reliable" : "VERDICT: not reliable");
        foreach (var stage in _stages)
        {
            text.AppendLine(stage.Detail.Length == 0 ? $"{stage.Name}: {stage.Outcome}" : $"{stage.Name}: {stage.Outcome} - {stage.Detail}");
        }

        foreach (var note in _notes)
        {
            text.AppendLine(note);
        }

        return text.ToString();
    }
}
```

`tools/IconPositionProbe/Waiting.cs`:

```csharp
namespace DeskAI.IconProbe;

internal static class Waiting
{
    /// <summary>Asks <paramref name="probe"/> until it answers or <paramref name="timeout"/> passes.</summary>
    internal static async Task<T?> ForAsync<T>(Func<T?> probe, TimeSpan timeout, TimeSpan step, TimeProvider clock)
        where T : class
    {
        var deadline = clock.GetUtcNow() + timeout;
        while (true)
        {
            if (probe() is { } value)
            {
                return value;
            }

            if (clock.GetUtcNow() >= deadline)
            {
                return null;
            }

            await Task.Delay(step).ConfigureAwait(false);
        }
    }
}
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet build DeskAI.sln -c Release --no-restore` then
`dotnet test DeskAI.sln -c Release --no-build --no-restore`
Expected: 0 warnings; all tests pass, including the new `DeskAI.IconProbe.Tests`.

- [ ] **Step 6: Commit**

```bash
git add DeskAI.sln tools/IconPositionProbe tests/DeskAI.IconProbe.Tests
git commit -m "Add the icon-position probe's Sandbox guard, layout, checks, and report"
```

---

### Task 3: The shell connection and the probe run

**Files:**
- Create: `tools/IconPositionProbe/DesktopShellView.cs`
- Modify: `tools/IconPositionProbe/Program.cs` (replace the stub)

**Interfaces:**
- Consumes: everything Task 2 produces.
- Produces: `IconPositionProbe.exe <resultsFolder>` → writes `<resultsFolder>/report.txt`; exit
  code 0 reliable, 1 not reliable, 2 refused (not in Sandbox), 3 Desktop view not found.
- `sealed class DesktopShellView : IDisposable` with:
  `static DesktopShellView? TryOpen()`, `Point Spacing { get; }`,
  `IReadOnlyDictionary<string, Point> ReadPositions()`, `void Place(IReadOnlyDictionary<string, Point> targets)`,
  `uint ReadFlags()`, `void WriteFlags(uint mask, uint flags)`, `void Refresh()`,
  `const uint AutoArrange = 0x1`, `const uint SnapToGrid = 0x4`.

This task has no automated test: it can only run against a real shell view, which exists safely
only inside the Sandbox (Task 4). Its logic is kept thin; every decision is in Task 2's tested
parts.

- [ ] **Step 1: Write `DesktopShellView.cs`**

COM slot order matters: every interface lists its methods in the exact `shobjidl_core.h` order,
with never-called methods declared as parameterless placeholders to keep their slots.

```csharp
using System.Runtime.InteropServices;

namespace DeskAI.IconProbe;

/// <summary>
/// The Desktop's shell view: read and set icon positions and the Auto arrange flags.
/// Path: ShellWindows.FindWindowSW(SWC_DESKTOP) → top-level browser → active view → IFolderView2
/// (Raymond Chen, "Manipulating the positions of desktop icons", 2013-11-18).
/// </summary>
internal sealed class DesktopShellView : IDisposable
{
    internal const uint AutoArrange = 0x1;
    internal const uint SnapToGrid = 0x4;
    private const int CsidlDesktop = 0;
    private const int SwcDesktop = 8;
    private const int SwfoNeedDispatch = 1;
    private const uint SvsiPositionItem = 0x80;
    private const uint SigdnParentRelativeParsing = 0x80018001;
    private static readonly Guid ClsidShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid SidTopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IidShellBrowser = typeof(IShellBrowser).GUID;
    private static readonly Guid IidShellItem = typeof(IShellItem).GUID;

    private readonly IShellView _view;
    private readonly IFolderView2 _folder;

    private DesktopShellView(IShellView view)
    {
        _view = view;
        _folder = (IFolderView2)view;
        _folder.GetSpacing(out var spacing);
        Spacing = spacing;
    }

    internal Point Spacing { get; }

    /// <returns>The view, or null while Explorer has not made one yet.</returns>
    internal static DesktopShellView? TryOpen()
    {
        try
        {
            var windows = (IShellWindows)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidShellWindows, throwOnError: true)!)!;
            object location = CsidlDesktop;
            object root = null!;
            var dispatch = windows.FindWindowSW(ref location, ref root, SwcDesktop, out _, SwfoNeedDispatch);
            if (dispatch is not IServiceProvider provider)
            {
                return null;
            }

            var sid = SidTopLevelBrowser;
            var iid = IidShellBrowser;
            provider.QueryService(ref sid, ref iid, out var browserObject);
            ((IShellBrowser)browserObject).QueryActiveShellView(out var view);
            return new DesktopShellView(view);
        }
        catch (COMException)
        {
            return null;
        }
    }

    internal IReadOnlyDictionary<string, Point> ReadPositions()
    {
        var positions = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        _folder.ItemCount(0x2 /* SVGIO_ALLVIEW */, out var count);
        for (var i = 0; i < count; i++)
        {
            _folder.Item(i, out var pidl);
            try
            {
                _folder.GetItemPosition(pidl, out var point);
                positions[NameOf(i)] = point;
            }
            finally
            {
                Marshal.FreeCoTaskMem(pidl);
            }
        }

        return positions;
    }

    internal void Place(IReadOnlyDictionary<string, Point> targets)
    {
        _folder.ItemCount(0x2, out var count);
        var pidls = new List<IntPtr>();
        var points = new List<Point>();
        try
        {
            for (var i = 0; i < count; i++)
            {
                if (targets.TryGetValue(NameOf(i), out var target))
                {
                    _folder.Item(i, out var pidl);
                    pidls.Add(pidl);
                    points.Add(target);
                }
            }

            _folder.SelectAndPositionItems((uint)pidls.Count, [.. pidls], [.. points], SvsiPositionItem);
        }
        finally
        {
            pidls.ForEach(Marshal.FreeCoTaskMem);
        }
    }

    internal uint ReadFlags()
    {
        _folder.GetCurrentFolderFlags(out var flags);
        return flags;
    }

    internal void WriteFlags(uint mask, uint flags) => _folder.SetCurrentFolderFlags(mask, flags);

    internal void Refresh() => _view.Refresh();

    public void Dispose()
    {
        Marshal.ReleaseComObject(_view);
    }

    private string NameOf(int index)
    {
        var iid = IidShellItem;
        _folder.GetItem(index, ref iid, out var item);
        item.GetDisplayName(SigdnParentRelativeParsing, out var name);
        try
        {
            return Marshal.PtrToStringUni(name) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeCoTaskMem(name);
        }
    }

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        int Count { get; }
        void Item();
        void NewEnum();
        void Register();
        void RegisterPending();
        void Revoke();
        void OnNavigate();
        void OnActivated();
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW(ref object location, ref object locationRoot, int windowClass, out int hwnd, int options);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        void QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object result);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow();
        void ContextSensitiveHelp();
        void InsertMenusSB();
        void SetMenuSB();
        void RemoveMenusSB();
        void SetStatusTextSB();
        void EnableModelessSB();
        void TranslateAcceleratorSB();
        void BrowseObject();
        void GetViewStateStream();
        void GetControlWindow();
        void SendControlMsg();
        void QueryActiveShellView(out IShellView view);
    }

    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void GetWindow();
        void ContextSensitiveHelp();
        void TranslateAccelerator();
        void EnableModeless();
        void UIActivate();
        void Refresh();
    }

    [ComImport, Guid("1AF3A467-214F-4298-908E-06B03E0B39F9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView2
    {
        // IFolderView
        void GetCurrentViewMode();
        void SetCurrentViewMode();
        void GetFolder();
        void Item(int index, out IntPtr pidl);
        void ItemCount(uint flags, out int count);
        void Items();
        void GetSelectionMarkedItem();
        void GetFocusedItem();
        void GetItemPosition(IntPtr pidl, out Point point);
        void GetSpacing(out Point spacing);
        void GetDefaultSpacing();
        void GetAutoArrange();
        void SelectItem();
        void SelectAndPositionItems(uint count, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] pidls,
            [MarshalAs(UnmanagedType.LPArray)] Point[] points, uint flags);

        // IFolderView2
        void SetGroupBy();
        void GetGroupBy();
        void SetViewProperty();
        void GetViewProperty();
        void SetTileViewProperties();
        void SetExtendedTileViewProperties();
        void SetText();
        void SetCurrentFolderFlags(uint mask, uint flags);
        void GetCurrentFolderFlags(out uint flags);
        void GetSortColumnCount();
        void SetSortColumns();
        void GetSortColumns();
        void GetItem(int index, ref Guid riid, out IShellItem item);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler();
        void GetParent();
        void GetDisplayName(uint sigdn, out IntPtr name);
    }
}
```

Note for the implementer: `Point` is the Task 2 record struct of two `int`s, which has the same
layout as Win32 `POINT`. If the analyzer flags `IServiceProvider` as clashing with
`System.IServiceProvider`, keep the nested private name — it is private to this class.

- [ ] **Step 2: Write `Program.cs`**

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using DeskAI.IconProbe;

// ADR 0043. Refuse first: nothing below may run outside Windows Sandbox.
if (SandboxGuard.Check(Environment.UserName) is { } refusal)
{
    Console.Error.WriteLine(refusal);
    return 2;
}

var results = args.Length == 1 ? args[0] : throw new ArgumentException("Usage: IconPositionProbe <results folder>");
var report = new ProbeReport();
var clock = TimeProvider.System;
var wait = TimeSpan.FromSeconds(60);
var step = TimeSpan.FromMilliseconds(500);

var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
var names = Enumerable.Range(1, 12).Select(i => $"deskai-probe-{i:D2}.txt").ToList();
foreach (var name in names)
{
    File.WriteAllText(Path.Combine(desktop, name), "Generated by the DeskAI icon-position probe.");
}

var view = await Waiting.ForAsync(
    () => DesktopShellView.TryOpen() is { } v && names.All(v.ReadPositions().ContainsKey) ? v : null, wait, step, clock);
if (view is null)
{
    report.Add(new Stage("find desktop view", StageOutcome.Failed, "Explorer did not show the probe's icons within 60 seconds"));
    return Finish(3);
}

report.Note($"windows: {Environment.OSVersion.VersionString}");
report.Note($"dpi: {GetDpiForSystem()}");
report.Note($"icon spacing: {view.Spacing.X} x {view.Spacing.Y}");
var area = WorkArea();
report.Note($"work area: {area.Left},{area.Top} - {area.Right},{area.Bottom}");

var originalFlags = view.ReadFlags();
var originalPositions = view.ReadPositions();
report.Note($"auto arrange at start: {(originalFlags & DesktopShellView.AutoArrange) != 0}");
report.Note($"icons at start: {originalPositions.Count}");

// Place.
view.WriteFlags(DesktopShellView.AutoArrange | DesktopShellView.SnapToGrid, 0);
var targets = names.Zip(ProbeLayout.Targets(names.Count, view.Spacing, area)).ToDictionary(p => p.First, p => p.Second);
view.Place(targets);
await Task.Delay(TimeSpan.FromSeconds(2));
Record("place", PositionCheck.Differences(targets, view.ReadPositions(), view.Spacing));

// Refresh.
view.Refresh();
await Task.Delay(TimeSpan.FromSeconds(3));
Record("refresh", PositionCheck.Differences(targets, view.ReadPositions(), view.Spacing));

// Explorer restart: recorded for the owner, not required (ADR 0043).
view.Dispose();
foreach (var explorer in Process.GetProcessesByName("explorer"))
{
    explorer.Kill();
}

Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
view = await Waiting.ForAsync(
    () => DesktopShellView.TryOpen() is { } v && names.All(v.ReadPositions().ContainsKey) ? v : null, wait, step, clock);
if (view is null)
{
    report.Add(new Stage("explorer restart", StageOutcome.Failed, "Explorer did not come back within 60 seconds"));
    report.Add(new Stage("put back", StageOutcome.Skipped, "no desktop view"));
    return Finish(1);
}

await Task.Delay(TimeSpan.FromSeconds(3));
Record("explorer restart", PositionCheck.Differences(targets, view.ReadPositions(), view.Spacing));

// Put back: every original position, then the original flags.
view.Place(originalPositions);
view.WriteFlags(DesktopShellView.AutoArrange | DesktopShellView.SnapToGrid, originalFlags);
await Task.Delay(TimeSpan.FromSeconds(2));
var putBack = PositionCheck.Differences(originalPositions, view.ReadPositions(), view.Spacing).ToList();
if ((view.ReadFlags() & (DesktopShellView.AutoArrange | DesktopShellView.SnapToGrid))
    != (originalFlags & (DesktopShellView.AutoArrange | DesktopShellView.SnapToGrid)))
{
    putBack.Add("auto arrange / snap to grid not restored");
}

Record("put back", putBack);
view.Dispose();
return Finish(report.IsReliable ? 0 : 1);

void Record(string stage, IReadOnlyList<string> differences) =>
    report.Add(differences.Count == 0
        ? new Stage(stage, StageOutcome.Passed, string.Empty)
        : new Stage(stage, StageOutcome.Failed, string.Join("; ", differences)));

int Finish(int code)
{
    Directory.CreateDirectory(results);
    File.WriteAllText(Path.Combine(results, "report.txt"), report.Render());
    Console.WriteLine(report.Render());
    return code;
}

static Rect WorkArea()
{
    var rect = new int[4];
    return SystemParametersInfo(0x0030 /* SPI_GETWORKAREA */, 0, rect, 0)
        ? new Rect(rect[0], rect[1], rect[2], rect[3])
        : throw new InvalidOperationException("Windows did not give the work area.");
}

[DllImport("user32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool SystemParametersInfo(uint action, uint parameter, [Out] int[] value, uint flags);

[DllImport("user32.dll")]
static extern uint GetDpiForSystem();
```

Note for the implementer: `Environment.GetFolderPath` uses the known-folder API, as `AGENTS.md`
requires. Local functions with `[DllImport]` in top-level statements need C# 9+ (the repo uses
`latest`); if the analyzer objects, move the two imports into a small `static partial class
NativeMethods` in the same project.

- [ ] **Step 3: Build and check the guard by hand on this PC**

Run: `dotnet build DeskAI.sln -c Release --no-restore`
Expected: 0 warnings.

Run the probe here, where it must refuse:
`tools/IconPositionProbe/bin/Release/net10.0-windows/win-x64/IconPositionProbe.exe artifacts/icon-probe/results`
Expected: prints "This probe runs only inside Windows Sandbox…", exit code 2, and **no**
`deskai-probe-*.txt` appears anywhere (check `artifacts/icon-probe/results` does not exist).
This is the one run allowed outside the Sandbox, and it only proves the refusal.

- [ ] **Step 4: Run all tests and format**

Run the three verification commands. Expected: all pass, format clean.

- [ ] **Step 5: Commit**

```bash
git add tools/IconPositionProbe
git commit -m "Connect the icon-position probe to the Desktop view, run only in Sandbox"
```

---

### Task 4: The Sandbox script, the run, and recording the answer

**Files:**
- Create: `tools/IconPositionProbe/Run-InSandbox.ps1`
- Modify: `docs/decisions/0043-desktop-icon-positions.md` (Status → Accepted or Rejected, results)
- Modify: `docs/superpowers/specs/2026-09-24-desktop-studio-design.md` ("Feasibility check first": outcome; "24H2" → build 26200)
- Modify: `docs/HANDOFF.md` (Start here: probe outcome and next task)
- Modify: `docs/DEVELOPMENT.md` (how to run the probe, one short section)

**Interfaces:**
- Consumes: `IconPositionProbe.exe <resultsFolder>` and its exit codes from Task 3.

- [ ] **Step 1: Write `Run-InSandbox.ps1`**

```powershell
#requires -Version 5.1
<#
.SYNOPSIS
  Runs the icon-position probe (ADR 0043) inside Windows Sandbox. Nothing runs on this PC's Desktop.
.DESCRIPTION
  Publishes the probe self-contained into artifacts/icon-probe/app, writes a .wsb that maps that
  folder read-only and artifacts/icon-probe/results writable, turns networking off, and starts the
  Sandbox. The probe writes results/report.txt. Close the Sandbox afterwards; it is thrown away.
#>
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$root = Join-Path $repo 'artifacts\icon-probe'
$app = Join-Path $root 'app'
$results = Join-Path $root 'results'
$sandbox = Join-Path $env:SystemRoot 'System32\WindowsSandbox.exe'

if (-not (Test-Path $sandbox)) {
    throw 'Windows Sandbox is not turned on. Turn on "Windows Sandbox" in Windows Features, restart, and run this again.'
}

dotnet publish (Join-Path $PSScriptRoot 'IconPositionProbe.csproj') -c Release --self-contained -o $app
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
if (Test-Path (Join-Path $results 'report.txt')) { Remove-Item (Join-Path $results 'report.txt') }
New-Item -ItemType Directory -Force $results | Out-Null

$wsb = @"
<Configuration>
  <Networking>Disable</Networking>
  <ClipboardRedirection>Disable</ClipboardRedirection>
  <PrinterRedirection>Disable</PrinterRedirection>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$app</HostFolder>
      <SandboxFolder>C:\Probe</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <HostFolder>$results</HostFolder>
      <SandboxFolder>C:\ProbeResults</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>C:\Probe\IconPositionProbe.exe C:\ProbeResults</Command>
  </LogonCommand>
</Configuration>
"@
$wsbPath = Join-Path $root 'icon-probe.wsb'
Set-Content -Path $wsbPath -Value $wsb -Encoding utf8
Start-Process $sandbox -ArgumentList "`"$wsbPath`""
Write-Host "Sandbox started. When it finishes, read $results\report.txt, then close the Sandbox."
```

The `.wsb` holds absolute host paths, so it lives only under the ignored `artifacts/` folder and
is never committed.

- [ ] **Step 2: Check the generated `.wsb` before the run**

Run the script once the owner has turned the Sandbox on. Before the Sandbox finishes starting,
read `artifacts/icon-probe/icon-probe.wsb` and confirm: networking `Disable`, the app folder
`ReadOnly` `true`, and only `artifacts\icon-probe\results` writable. If any differ, close the
Sandbox and fix the script.

- [ ] **Step 3: Read the report**

Wait for `artifacts/icon-probe/results/report.txt` (a minute or two). Expected shape:

```text
VERDICT: reliable
place: Passed
refresh: Passed
explorer restart: Passed
put back: Passed
windows: Microsoft Windows NT 10.0.26200.0
dpi: 96
icon spacing: 75 x 100
work area: 0,0 - 1904,1001
auto arrange at start: True
icons at start: 13
```

If the probe hangs past 3 minutes, note it as "not reliable: probe did not finish", close the
Sandbox, and record that. Run the probe **twice** (close and restart the Sandbox between runs) so
one lucky run does not decide it. Optionally, the owner resizes the Sandbox window to a different
scaling and runs a third time.

- [ ] **Step 4: Record the answer**

- ADR 0043: Status `Accepted` (both runs reliable) or `Rejected`; paste both reports' stage lines
  and notes under a new "## Probe results (2026-09-24)" heading; if "explorer restart" failed,
  write that the next plan must re-apply after Explorer restarts or tell the person, and that
  the owner decides which.
- Spec "Feasibility check first": one sentence with the outcome and the ADR link; "Windows 11
  24H2" → "the owner's Windows 11 (build 26200)".
- `docs/DEVELOPMENT.md`: a short "Icon-position probe" section: turn on Windows Sandbox, run
  `tools/IconPositionProbe/Run-InSandbox.ps1`, read `artifacts/icon-probe/results/report.txt`;
  never run the exe on your own PC (it refuses anyway).
- `docs/HANDOFF.md` Start here: the outcome and the next task — if Accepted, "write the plan for
  Keep together (Core contract, snapshot-first, ask before Auto arrange, Put back)"; if Rejected,
  "tell the owner the three screen cards are dropped; next is Clear old stuff and Folder by
  group".

- [ ] **Step 5: Verify and commit**

Run the three verification commands. Expected: all pass, format clean. Then:

```bash
git add tools/IconPositionProbe/Run-InSandbox.ps1 docs/decisions/0043-desktop-icon-positions.md docs/superpowers/specs/2026-09-24-desktop-studio-design.md docs/DEVELOPMENT.md docs/HANDOFF.md
git commit -m "Run the icon-position probe in Windows Sandbox and record the answer"
```

Tell the owner the verdict in plain words: whether DeskAI can arrange Desktop icons, whether
they stay after a restart of Explorer, and what that means for Keep together, Make zones, and
Name the zones.
