using System.Diagnostics;

namespace DeskAI.Infrastructure.Launching;

/// <summary>
/// The Windows shell's "open" action for a validated file, or File Explorer with it selected.
/// </summary>
/// <remarks>
/// Called only by <see cref="WindowsFileLauncher"/>, after its checks. Explorer is started by its
/// full path under the Windows folder, never found through PATH.
/// </remarks>
public sealed class WindowsShellStarter : IShellStarter
{
    public void OpenWithUsualApp(string fullPath)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = fullPath,
            UseShellExecute = true,
            Verb = "open",
        });
    }

    public void ShowInFolder(string fullPath)
    {
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = explorer,
            Arguments = $"/select,\"{fullPath}\"",
            UseShellExecute = false,
        });
    }
}
