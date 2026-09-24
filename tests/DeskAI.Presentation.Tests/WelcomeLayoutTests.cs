namespace DeskAI.Presentation.Tests;

/// <summary>
/// The welcome's wiring in the window, which the view model tests cannot see: it opens only
/// when the shell claims the first showing, a Connect press goes through Home's own "Connect
/// your …?" question before anything connects, and Privacy and AI can open it again.
/// </summary>
public sealed class WelcomeLayoutTests
{
    [Fact]
    public void The_window_opens_the_welcome_only_after_claiming_it_and_connects_only_after_Homes_question()
    {
        var window = Read("src", "DeskAI.App", "MainWindow.xaml.cs");

        Assert.Contains("ClaimFirstWelcomeAsync()", window, StringComparison.Ordinal);
        var ask = window.IndexOf("PersonalFolderDialogs.ConfirmConnectAsync", StringComparison.Ordinal);
        var connect = window.IndexOf("ConnectChosenAsync()", StringComparison.Ordinal);
        Assert.True(ask > 0 && connect > ask, "The welcome must ask Home's question before connecting.");
        Assert.DoesNotContain("MarkShownAsync", window, StringComparison.Ordinal);
    }

    [Fact]
    public void Privacy_and_AI_has_a_button_that_opens_the_welcome_again()
    {
        Assert.Contains("Content=\"Show the welcome again\"", Read("src", "DeskAI.App", "Views", "SettingsPage.xaml"), StringComparison.Ordinal);
        Assert.Contains("ShowWelcomeAsync()", Read("src", "DeskAI.App", "Views", "SettingsPage.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_pop_up_is_closed_with_Skip_and_moves_with_Back_and_the_view_models_Next()
    {
        var dialog = Read("src", "DeskAI.App", "Views", "WelcomeDialog.cs");

        Assert.Contains("CloseButtonText = \"Skip\"", dialog, StringComparison.Ordinal);
        Assert.Contains("SecondaryButtonText = \"Back\"", dialog, StringComparison.Ordinal);
        Assert.Contains("welcome.Next()", dialog, StringComparison.Ordinal);
        Assert.Contains("welcome.Choose(", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectAsync", dialog, StringComparison.Ordinal);
    }

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
