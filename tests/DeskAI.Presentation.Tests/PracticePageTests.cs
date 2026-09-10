using DeskAI.App.ViewModels;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Practice mode: the tidy-up run on generated sample files, and undo.
/// </summary>
public sealed class PracticePageTests
{
    [Fact]
    public async Task The_practice_preview_lists_suggested_moves_before_anything_runs()
    {
        await using var app = await TestApp.StartAsync();
        var organize = app.Get<PracticeViewModel>();

        await organize.InitializeAsync();

        Assert.NotEmpty(organize.Operations);
        Assert.True(organize.SelectedOperationCount > 0);
        Assert.True(organize.ExecuteDemoCommand.CanExecute(null));
        Assert.False(organize.UndoDemoCommand.CanExecute(null));
    }

    [Fact]
    public async Task Running_the_practice_moves_sample_files_and_undo_puts_them_back()
    {
        await using var app = await TestApp.StartAsync();
        var organize = app.Get<PracticeViewModel>();
        await organize.InitializeAsync();
        var selected = organize.SelectedOperationCount;

        await organize.ExecuteDemoCommand.ExecuteAsync(null);

        Assert.StartsWith("Done", organize.ResultMessage, StringComparison.Ordinal);
        Assert.Contains($"{selected} sample file(s)", organize.ResultMessage, StringComparison.Ordinal);
        Assert.StartsWith(app.Directory.Path, organize.DemoRoot, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Sample organization", organize.ActivityTitle);
        Assert.True(organize.UndoDemoCommand.CanExecute(null));

        await organize.UndoDemoCommand.ExecuteAsync(null);

        Assert.StartsWith("Undo complete", organize.ResultMessage, StringComparison.Ordinal);
        // The latest activity is the undo itself, and it must not read as partial: the
        // sample workspace already had a Documents folder, which undo correctly leaves.
        Assert.Equal("Undo", organize.ActivityTitle);
        Assert.Equal($"Completed · {selected} file(s)", organize.ActivityMessage);
    }

    [Fact]
    public async Task Unticking_everything_disables_the_run_button()
    {
        await using var app = await TestApp.StartAsync();
        var organize = app.Get<PracticeViewModel>();
        await organize.InitializeAsync();

        organize.ClearSelectionCommand.Execute(null);

        Assert.Equal(0, organize.SelectedOperationCount);
        Assert.False(organize.ExecuteDemoCommand.CanExecute(null));
    }
}
