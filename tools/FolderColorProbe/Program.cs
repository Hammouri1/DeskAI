using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DeskAI.FolderColorProbe;
using DeskAI.IconProbe;

// ADR 0046. Refuse first: nothing below may run outside Windows Sandbox.
if (SandboxGuard.Check(Environment.UserName) is { } refusal)
{
    Console.Error.WriteLine(refusal);
    return 2;
}

var results = args.Length == 1 ? args[0] : throw new ArgumentException("Usage: FolderColorProbe <results folder>");
Directory.CreateDirectory(results);
SetProcessDpiAwarenessContext(-4 /* DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 */);

const int Tolerance = 40;
const int ColouredAtLeast = 200;
const int PlainAtMost = 20;
const double LikeStartAtMost = 20;
var magenta = new Rgb(230, 0, 230);
var green = new Rgb(0, 180, 0);
var report = new ColorProbeReport();
var clock = TimeProvider.System;
var settle = TimeSpan.FromSeconds(3);
var lastSeen = "nothing yet";

var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
var iconFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskAIColorProbe");
var icons = new Dictionary<Rgb, string>
{
    [magenta] = Path.Combine(iconFolder, "magenta.ico"),
    [green] = Path.Combine(iconFolder, "green.ico"),
};

// The probe's own four folders: the only things it colours and puts back.
var folders = new List<(string Name, Rgb Color)>
{
    ("Color probe plain A", magenta),
    ("Color probe plain B", green),
    ("Color probe read-only", magenta),
    ("Color probe custom", green),
};
string PathOf(string name) => Path.Combine(desktop, name);

try
{
    foreach (var (name, _) in folders)
    {
        Directory.CreateDirectory(PathOf(name));
    }

    var readOnly = PathOf("Color probe read-only");
    File.SetAttributes(readOnly, File.GetAttributes(readOnly) | FileAttributes.ReadOnly);

    // A folder that already has its own desktop.ini: another icon, an info tip, another section.
    var custom = PathOf("Color probe custom");
    var customIni = Path.Combine(custom, FolderState.IniName);
    File.WriteAllText(
        customIni,
        "[.ShellClassInfo]\r\nIconResource=C:\\Windows\\System32\\shell32.dll,3\r\nInfoTip=Made by the DeskAI folder-color probe\r\n[ViewState]\r\nMode=\r\nVid=\r\nFolderType=Generic\r\n",
        Encoding.Unicode);
    File.SetAttributes(customIni, FileAttributes.Hidden | FileAttributes.System);
    File.SetAttributes(custom, File.GetAttributes(custom) | FileAttributes.ReadOnly);
    FolderShell.Changed(custom);

    Directory.CreateDirectory(iconFolder);
    foreach (var (color, file) in icons)
    {
        File.WriteAllBytes(file, FolderIcon.Create(color));
    }

    // Restart Explorer once before the start picture. The first restart re-sorts the Desktop's
    // icons (run 1: Microsoft Edge moved below the folders, so every folder moved up one place
    // and could not be compared with the start); later restarts keep that order.
    var waitStarted = clock.GetUtcNow();
    var view = await OpenViewAsync();
    report.Note($"first look: {(view is null ? "no folders" : "folders shown")} after {(clock.GetUtcNow() - waitStarted).TotalSeconds:F0} s");
    view?.Dispose();
    view = view is null ? null : await RestartExplorerAsync();
    if (view is null)
    {
        report.Add(new Stage("find desktop view", StageOutcome.Failed, $"Explorer did not show the probe's folders within 120 seconds; last look: {lastSeen}"));
        return Finish(3);
    }

    report.Note($"windows: {Environment.OSVersion.VersionString}");
    report.Note($"dpi: {GetDpiForSystem()}");
    report.Note($"icon spacing: {view.Spacing.X} x {view.Spacing.Y}");

    var before = folders.ToDictionary(f => f.Name, f => FolderState.Read(PathOf(f.Name)));
    var locationsBefore = folders.ToDictionary(f => f.Name, f => FolderShell.IconLocation(PathOf(f.Name)));
    var customTextBefore = DesktopIni.Text(before["Color probe custom"].IniBytes!);
    foreach (var (name, _) in folders)
    {
        report.Note($"shell icon before, {name}: {locationsBefore[name]}");
    }

    await Task.Delay(settle);
    var start = Pixels("before color", view, "0-start.png", Expect.Start, start: null);

    // Colour.
    var colourProblems = new List<string>();
    foreach (var (name, color) in folders)
    {
        try
        {
            FolderShell.SetIcon(PathOf(name), icons[color], 0);
        }
        catch (COMException ex)
        {
            colourProblems.Add($"{name}: Windows refused the icon ({ex.Message})");
        }

        FolderShell.Changed(PathOf(name));
    }

    foreach (var (name, color) in folders)
    {
        var state = FolderState.Read(PathOf(name));
        var text = state.IniBytes is null ? string.Empty : DesktopIni.Text(state.IniBytes);
        var resource = DesktopIni.Value(text, ".ShellClassInfo", "IconResource");

        // Windows may store the path with %LOCALAPPDATA% and similar left in.
        if (resource is null || !Environment.ExpandEnvironmentVariables(resource).StartsWith(icons[color], StringComparison.OrdinalIgnoreCase))
        {
            colourProblems.Add($"{name}: desktop.ini icon is \"{resource}\"");
        }

        var location = FolderShell.IconLocation(PathOf(name));
        if (!Environment.ExpandEnvironmentVariables(location).StartsWith(icons[color], StringComparison.OrdinalIgnoreCase))
        {
            colourProblems.Add($"{name}: shell icon is {location}");
        }

        report.Note($"after color, {name}: folder {state.Attributes}, desktop.ini {state.IniAttributes}, shell icon {location}");
    }

    var customTextAfter = DesktopIni.Text(FolderState.Read(custom).IniBytes ?? []);
    var lost = DesktopIni.LinesWithoutIcon(customTextBefore).Except(DesktopIni.LinesWithoutIcon(customTextAfter), StringComparer.OrdinalIgnoreCase).ToList();
    if (lost.Count > 0)
    {
        colourProblems.Add($"custom folder lost lines: {string.Join(" | ", lost)}");
    }

    report.Note($"custom desktop.ini after color: {customTextAfter.ReplaceLineEndings(" | ")}");
    Record("color", colourProblems);

    await Task.Delay(settle);
    Pixels("color seen before refresh", view, "1-colored.png", Expect.Colour, start: null);

    // Refresh (what F5 does).
    view.Refresh();
    await Task.Delay(settle);
    Pixels("refresh", view, "2-refresh.png", Expect.Colour, start: null);

    // Explorer restart.
    view.Dispose();
    view = await RestartExplorerAsync();
    if (view is null)
    {
        report.Add(new Stage("explorer restart", StageOutcome.Failed, $"Explorer did not come back within 120 seconds; last look: {lastSeen}"));
    }
    else
    {
        await Task.Delay(settle);
        Pixels("explorer restart", view, "3-restart.png", Expect.Colour, start: null);
    }

    // Put back from the snapshot.
    var putBackProblems = new List<string>();
    foreach (var (name, _) in folders)
    {
        FolderState.Restore(PathOf(name), before[name]);
        FolderShell.Changed(PathOf(name));
    }

    foreach (var (name, _) in folders)
    {
        putBackProblems.AddRange(FolderState.Differences(before[name], FolderState.Read(PathOf(name))).Select(d => $"{name}: {d}"));
        var location = FolderShell.IconLocation(PathOf(name));
        if (!string.Equals(location, locationsBefore[name], StringComparison.OrdinalIgnoreCase))
        {
            // Our own process may still hold the old answer for a moment; ask once more.
            await Task.Delay(TimeSpan.FromSeconds(1));
            report.Note($"shell icon after put back, {name}: first {location}");
            location = FolderShell.IconLocation(PathOf(name));
        }

        if (!string.Equals(location, locationsBefore[name], StringComparison.OrdinalIgnoreCase))
        {
            putBackProblems.Add($"{name}: shell icon is {location}, was {locationsBefore[name]}");
        }
    }

    Record("put back", putBackProblems);
    if (view is not null)
    {
        await Task.Delay(settle);
        Pixels("put back before refresh", view, "4-put-back.png", Expect.LikeStart, start);
        view.Refresh();
        await Task.Delay(settle);
        Pixels("put back after refresh", view, "5-put-back-refresh.png", Expect.LikeStart, start);

        // What a person would see after removing DeskAI: the icon file is gone.
        var first = PathOf(folders[0].Name);
        FolderShell.SetIcon(first, icons[magenta], 0);
        FolderShell.Changed(first);
        view.Refresh();
        await Task.Delay(settle);
        File.Delete(icons[magenta]);
        FolderShell.Changed(first);
        view.Refresh();
        await Task.Delay(settle);
        var shot = Capture("6-icon-missing.png");
        var left = shot is null ? -1 : Count(view, shot, folders[0].Name, magenta);
        report.Add(new Stage(
            "icon file missing",
            StageOutcome.Passed,
            $"{left} magenta pixels left on {folders[0].Name}; shell icon {FolderShell.IconLocation(first)}; see 6-icon-missing.png"));
        FolderState.Restore(first, before[folders[0].Name]);
        FolderShell.Changed(first);
        view.Dispose();
    }
    else
    {
        report.Add(new Stage("put back after refresh", StageOutcome.Skipped, "no desktop view"));
    }

    return Finish(report.IsReliable ? 0 : 1);
}
catch (Exception ex)
{
    report.Note($"stopped: {ex.GetType().Name}: {ex.Message}");
    return Finish(1);
}

// Windows may bring the shell back by itself; starting a second explorer.exe then opens a
// File Explorer window over the icons (ADR 0046 review).
async Task<DesktopShellView?> RestartExplorerAsync()
{
    foreach (var explorer in Process.GetProcessesByName("explorer"))
    {
        explorer.Kill();
        await explorer.WaitForExitAsync();
    }

    var cameBack = await Waiting.ForAsync(
        () => Process.GetProcessesByName("explorer").FirstOrDefault(), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(500), clock);
    report.Note($"explorer came back by itself: {cameBack is not null}");
    if (cameBack is null)
    {
        Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
    }

    return await OpenViewAsync();
}

async Task<DesktopShellView?> OpenViewAsync() =>
    await Waiting.ForAsync(TryView, TimeSpan.FromSeconds(120), TimeSpan.FromMilliseconds(500), clock);

// While Explorer is still starting, reading its view can fail; that means "not ready yet".
// What the last try saw goes into the report if the wait runs out (run 2 stopped here silently).
DesktopShellView? TryView()
{
    if (DesktopShellView.TryOpen() is not { } view)
    {
        lastSeen = "no Desktop view yet";
        return null;
    }

    try
    {
        var positions = view.ReadPositions();
        if (folders.All(f => positions.ContainsKey(f.Name)))
        {
            return view;
        }

        lastSeen = $"{positions.Count} items: {string.Join(", ", positions.Keys)}";
    }
    catch (Exception ex) when (ex is COMException or InvalidComObjectException)
    {
        lastSeen = $"reading the view failed: {ex.GetType().Name}: {ex.Message}";
    }

    view.Dispose();
    return null;
}

// Checks each folder's icon area on screen and saves the picture for the owner either way.
// Returns the look to compare with later, or null when the screen could not be read.
StartLook? Pixels(string stage, DesktopShellView view, string picture, Expect expect, StartLook? start)
{
    var shot = Capture(picture);
    if (shot is null || !ColorCount.IsReadable(shot.Bgra))
    {
        report.Add(new Stage(stage, StageOutcome.Skipped, $"the screen could not be read; look at {picture}"));
        return null;
    }

    var positions = view.ReadPositions();
    var problems = new List<string>();
    var counts = new List<string>();
    foreach (var (name, color) in folders)
    {
        var count = Count(view, shot, name, color);
        counts.Add($"{name} {count}");
        if (expect == Expect.Colour ? count < ColouredAtLeast : count > PlainAtMost)
        {
            problems.Add($"{name}: {count} pixels of its colour");
        }

        if (expect != Expect.LikeStart)
        {
            continue;
        }

        // "No colour" alone would also pass for an icon hidden behind a window.
        if (start is null || start.Shot.Width != shot.Width || start.Shot.Height != shot.Height)
        {
            problems.Add($"{name}: no readable start picture to compare with");
        }
        else if (start.Positions[name] != positions[name])
        {
            problems.Add($"{name}: moved from {start.Positions[name]} to {positions[name]}, so it cannot be compared with the start");
        }
        else
        {
            var difference = ColorCount.MeanDifference(
                start.Shot.Bgra, positions[name], shot.Bgra, positions[name], shot.Width, shot.Height, view.Spacing);
            counts.Add($"{name} differs from the start by {difference:F1}");
            if (difference > LikeStartAtMost)
            {
                problems.Add($"{name}: looks different from the start ({difference:F1})");
            }
        }
    }

    report.Note($"{stage} pixels: {string.Join(", ", counts)}");
    Record(stage, problems);
    return new StartLook(shot, positions);
}

int Count(DesktopShellView view, ScreenCapture shot, string name, Rgb color)
{
    var at = view.ReadPositions()[name];
    var area = new Rect(at.X, at.Y, at.X + view.Spacing.X, at.Y + view.Spacing.Y);
    return ColorCount.Count(shot.Bgra, shot.Width, shot.Height, area, color, Tolerance);
}

ScreenCapture? Capture(string picture)
{
    try
    {
        var shot = ScreenCapture.Take();
        File.WriteAllBytes(Path.Combine(results, picture), shot.ToPng());
        return shot;
    }
    catch (InvalidOperationException ex)
    {
        report.Note($"{picture}: {ex.Message}");
        return null;
    }
}

void Record(string stage, IReadOnlyList<string> problems) =>
    report.Add(problems.Count == 0
        ? new Stage(stage, StageOutcome.Passed, string.Empty)
        : new Stage(stage, StageOutcome.Failed, string.Join("; ", problems)));

int Finish(int code)
{
    File.WriteAllText(Path.Combine(results, "report.txt"), report.Render());
    return code;
}

[DllImport("user32.dll")]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool SetProcessDpiAwarenessContext(IntPtr context);

[DllImport("user32.dll")]
static extern uint GetDpiForSystem();

internal enum Expect
{
    /// <summary>The first look, before anything changes.</summary>
    Start,

    /// <summary>Each folder shows its colour.</summary>
    Colour,

    /// <summary>No colour left, and each icon looks as it did at the start.</summary>
    LikeStart,
}

internal sealed record StartLook(ScreenCapture Shot, IReadOnlyDictionary<string, DeskAI.IconProbe.Point> Positions);
