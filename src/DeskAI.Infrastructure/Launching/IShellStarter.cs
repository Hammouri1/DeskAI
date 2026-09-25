namespace DeskAI.Infrastructure.Launching;

/// <summary>Starts the one thing quick search may start, for one already-validated full path.</summary>
/// <remarks>
/// A seam so that no test ever starts a real process. The shared registration holds
/// <see cref="NoShellStarter"/>; only the app registers <see cref="WindowsShellStarter"/>.
/// </remarks>
public interface IShellStarter
{
    void OpenWithUsualApp(string fullPath);

    void ShowInFolder(string fullPath);
}

/// <summary>Starts nothing. The default, so a DeskAI built without the app's Windows pieces can never start a process.</summary>
public sealed class NoShellStarter : IShellStarter
{
    public void OpenWithUsualApp(string fullPath) => throw new InvalidOperationException("Opening files is not available here.");

    public void ShowInFolder(string fullPath) => throw new InvalidOperationException("Opening files is not available here.");
}
