using System.Diagnostics;
using DeskAI.App.ViewModels;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// Times the everyday work on a generated folder of a few thousand files. It runs only when
/// <c>DESKAI_PERF</c> is set, so the ordinary suite never depends on how fast a machine is, and
/// its numbers are written to <c>docs/PERFORMANCE.md</c> by hand.
/// </summary>
/// <remarks>
/// Run it with <c>$env:DESKAI_PERF = "1"; dotnet test --project tests/DeskAI.Presentation.Tests -c Release</c>
/// and read the timings from the test output. Every file is generated under the test's own
/// temp folder; nothing personal is touched.
/// </remarks>
public sealed class PerformanceProbe
{
    public const int FileCount = 3000;

    [Fact]
    public async Task Connect_refresh_search_and_preview_a_tidy_on_a_few_thousand_files()
    {
        if (Environment.GetEnvironmentVariable("DESKAI_PERF") is null)
        {
            return;
        }

        await using var app = await TestApp.StartAsync();
        var folder = app.MakeFolder("Big");
        var endings = new[] { ".pdf", ".jpg", ".txt", ".zip", ".mp4", ".docx" };
        for (var i = 0; i < FileCount; i++)
        {
            app.MakeFile("Big", $"file-{i:D5}{endings[i % endings.Length]}", content: new string('x', 64 + i % 900));
        }

        var report = new List<string>();
        var search = app.Get<SearchViewModel>();
        await search.InitializeAsync();

        var watch = Stopwatch.StartNew();
        await search.ConnectFolderAsync(folder);
        report.Add($"connect {FileCount} files: {watch.ElapsedMilliseconds} ms");

        watch.Restart();
        await search.RefreshFolderCommand.ExecuteAsync(Assert.Single(search.Folders).Id);
        report.Add($"refresh: {watch.ElapsedMilliseconds} ms");

        watch.Restart();
        search.Phrase = "videos";
        await search.SearchCommand.ExecuteAsync(null);
        report.Add($"search: {watch.ElapsedMilliseconds} ms ({search.Results.Count} shown)");

        var home = app.Get<DashboardViewModel>();
        watch.Restart();
        await home.InitializeAsync();
        report.Add($"home summary: {watch.ElapsedMilliseconds} ms");

        var tidy = app.Get<TidyViewModel>();
        await app.Get<DeskAI.Core.Tidy.TidyPermissionService>().AllowAsync(Assert.Single(search.Folders).Id, TestContext.Current.CancellationToken);
        watch.Restart();
        await tidy.InitializeAsync();
        report.Add($"tidy preview: {watch.ElapsedMilliseconds} ms ({tidy.IncludedCount} ticked, limit {DeskAI.Core.Tidy.TidySuggestionService.MaxFilesPerTidy})");

        TestContext.Current.SendDiagnosticMessage(string.Join(Environment.NewLine, report));
        // Also beside the temp folder, because the test folder is deleted when the app is disposed.
        await File.WriteAllLinesAsync(Path.Combine(Path.GetTempPath(), "DeskAI-perf.txt"), report, TestContext.Current.CancellationToken);
        Console.WriteLine(string.Join(Environment.NewLine, report));
    }
}
