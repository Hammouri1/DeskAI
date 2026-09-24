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
var magenta = new Rgb(230, 0, 230);
var green = new Rgb(0, 180, 0);
var report = new ColorProbeReport();
var clock = TimeProvider.System;
var settle = TimeSpan.FromSeconds(3);

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
        "[.ShellClassInfo]\r\nIconResource=C:\\Windows\\System32\\shell32.dll,12\r\nInfoTip=Made by the DeskAI folder-color probe\r\n[ViewState]\r\nMode=\r\nVid=\r\nFolderType=Generic\r\n",
        Encoding.Unicode);
    File.SetAttributes(customIni, FileAttributes.Hidden | FileAttributes.System);
    File.SetAttributes(custom, File.GetAttributes(custom) | FileAttributes.ReadOnly);
    FolderShell.Changed(custom);

    Directory.CreateDirectory(iconFolder);
    foreach (var (color, file) in icons)
    {
        File.WriteAllBytes(file, FolderIcon.Create(color));
    }

    var view = await OpenViewAsync();
    if (view is null)
    {
        report.Add(new Stage("find desktop view", StageOutcome.Failed, "Explorer did not show the probe's folders within 60 seconds"));
        return Finish(3);
    }

    report.Note($"windows: {Environment.OSVersion.VersionString}");
    report.Note($"dpi: {GetDpiForSystem()}");
    report.Note($"icon spacing: {view.Spacing.X} x {view.Spacing.Y}");

    var before = folders.ToDictionary(f => f.Name, f => FolderState.Read(PathOf(f.Name)));
    var locationsBefore = folders.ToDictionary(f => f.Name, f => FolderShell.IconLocation(PathOf(f.Name)));
    var customTextBefore = IniText(before["Color probe custom"].IniBytes!);
    foreach (var (name, _) in folders)
    {
        report.Note($"shell icon before, {name}: {locationsBefore[name]}");
    }

    await Task.Delay(settle);
    Pixels("before color", view, "0-start.png", expectColour: false);

    // Colour.
    foreach (var (name, color) in folders)
    {
        FolderShell.SetIcon(PathOf(name), icons[color], 0);
        FolderShell.Changed(PathOf(name));
    }

    var colourProblems = new List<string>();
    foreach (var (name, color) in folders)
    {
        var state = FolderState.Read(PathOf(name));
        var text = state.IniBytes is null ? string.Empty : IniText(state.IniBytes);
        var resource = DesktopIni.Value(text, ".ShellClassInfo", "IconResource");
        if (resource is null || !resource.StartsWith(icons[color], StringComparison.OrdinalIgnoreCase))
        {
            colourProblems.Add($"{name}: desktop.ini icon is \"{resource}\"");
        }

        var location = FolderShell.IconLocation(PathOf(name));
        if (!location.StartsWith(icons[color], StringComparison.OrdinalIgnoreCase))
        {
            colourProblems.Add($"{name}: shell icon is {location}");
        }

        report.Note($"after color, {name}: folder {state.Attributes}, desktop.ini {state.IniAttributes}, shell icon {location}");
    }

    var customTextAfter = IniText(FolderState.Read(custom).IniBytes ?? []);
    var lost = DesktopIni.LinesWithoutIcon(customTextBefore).Except(DesktopIni.LinesWithoutIcon(customTextAfter), StringComparer.OrdinalIgnoreCase).ToList();
    if (lost.Count > 0)
    {
        colourProblems.Add($"custom folder lost lines: {string.Join(" | ", lost)}");
    }

    report.Note($"custom desktop.ini after color: {customTextAfter.ReplaceLineEndings(" | ")}");
    Record("color", colourProblems);

    await Task.Delay(settle);
    Pixels("color seen before refresh", view, "1-colored.png", expectColour: true);

    // Refresh (what F5 does).
    view.Refresh();
    await Task.Delay(settle);
    Pixels("refresh", view, "2-refresh.png", expectColour: true);

    // Explorer restart.
    view.Dispose();
    foreach (var explorer in Process.GetProcessesByName("explorer"))
    {
        explorer.Kill();
    }

    Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
    view = await OpenViewAsync();
    if (view is null)
    {
        report.Add(new Stage("explorer restart", StageOutcome.Failed, "Explorer did not come back within 60 seconds"));
    }
    else
    {
        await Task.Delay(settle);
        Pixels("explorer restart", view, "3-restart.png", expectColour: true);
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
            putBackProblems.Add($"{name}: shell icon is {location}, was {locationsBefore[name]}");
        }
    }

    Record("put back", putBackProblems);
    if (view is not null)
    {
        await Task.Delay(settle);
        Pixels("put back before refresh", view, "4-put-back.png", expectColour: false);
        view.Refresh();
        await Task.Delay(settle);
        Pixels("put back after refresh", view, "5-put-back-refresh.png", expectColour: false);

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

async Task<DesktopShellView?> OpenViewAsync() =>
    await Waiting.ForAsync(
        () => DesktopShellView.TryOpen() is { } v && folders.All(f => v.ReadPositions().ContainsKey(f.Name)) ? v : null,
        TimeSpan.FromSeconds(60),
        TimeSpan.FromMilliseconds(500),
        clock);

// Checks each folder's icon area on screen: its colour must be there (or gone), and the
// picture is saved for the owner either way.
void Pixels(string stage, DesktopShellView view, string picture, bool expectColour)
{
    var shot = Capture(picture);
    if (shot is null || !ColorCount.IsReadable(shot.Bgra))
    {
        report.Add(new Stage(stage, StageOutcome.Skipped, $"the screen could not be read; look at {picture}"));
        return;
    }

    var problems = new List<string>();
    var counts = new List<string>();
    foreach (var (name, color) in folders)
    {
        var count = Count(view, shot, name, color);
        counts.Add($"{name} {count}");
        if (expectColour ? count < ColouredAtLeast : count > PlainAtMost)
        {
            problems.Add($"{name}: {count} pixels of its colour");
        }
    }

    report.Note($"{stage} pixels: {string.Join(", ", counts)}");
    Record(stage, problems);
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

// desktop.ini is UTF-16 when it starts with a byte-order mark, otherwise the ANSI code page;
// the probe's own paths are plain ASCII either way.
static string IniText(byte[] bytes) =>
    bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE
        ? Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2)
        : Encoding.Latin1.GetString(bytes);

[DllImport("user32.dll")]
[return: MarshalAs(UnmanagedType.Bool)]
static extern bool SetProcessDpiAwarenessContext(IntPtr context);

[DllImport("user32.dll")]
static extern uint GetDpiForSystem();
