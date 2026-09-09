using DeskAI.Core.Classification;
using DeskAI.Core.Indexing;
using DeskAI.Core.Search;

namespace DeskAI.Core.Tests;

public sealed class OrganizationHealthCalculatorTests
{
    [Fact]
    public void Evaluate_ReportsNotMeasuredWhenNoFolderIsConnected()
    {
        var health = OrganizationHealthCalculator.Evaluate(StorageSummary.Empty, DuplicateReport.Empty);

        Assert.False(health.IsMeasured);
        Assert.Equal(HealthBand.NotMeasured, health.Band);
        Assert.Empty(health.Components);
    }

    /// <summary>
    /// A connected folder that has been read but holds nothing is not unhealthy, and it is
    /// not healthy either. Scoring an empty folder would invent a judgement from no evidence.
    /// </summary>
    [Fact]
    public void Evaluate_ReportsNotMeasuredWhenTheFolderHoldsNothing()
    {
        var summary = StorageSummary.Empty with { FoldersIncluded = 1 };

        var health = OrganizationHealthCalculator.Evaluate(summary, DuplicateReport.Empty);

        Assert.False(health.IsMeasured);
    }

    [Fact]
    public void Evaluate_ScoresATidyFolderHighly()
    {
        var summary = Summary(totalBytes: 1_000_000, categories: [Usage(FileCategory.Documents, 10, 1_000_000)]);

        var health = OrganizationHealthCalculator.Evaluate(summary, DuplicateReport.Empty);

        Assert.True(health.IsMeasured);
        Assert.Equal(100, health.Score);
        Assert.Equal(HealthBand.Good, health.Band);
    }

    [Fact]
    public void Evaluate_LowersTheScoreWhenMuchOfTheSpaceMightBeDuplicated()
    {
        var summary = Summary(totalBytes: 1_000_000, categories: [Usage(FileCategory.Documents, 10, 1_000_000)]);
        var duplicates = Report(reclaimableBytes: 400_000, fileCount: 4);

        var health = OrganizationHealthCalculator.Evaluate(summary, duplicates);

        var copies = Component(health, HealthComponentKind.PossibleCopies);
        Assert.Equal(0, copies.Score);
        Assert.True(health.Score < 100);
    }

    /// <summary>
    /// A settled archive is not a mess. Nearly everything in one is old by definition, so a
    /// folder whose only finding is age must not be flagged: that would be the score saying
    /// moving more files is always better, which is exactly what it must not say.
    /// </summary>
    [Fact]
    public void Evaluate_DoesNotFlagAnArchiveWhereEverythingIsSimplyOld()
    {
        var summary = Summary(
            totalBytes: 1_000_000,
            categories: [Usage(FileCategory.Videos, 100, 1_000_000)],
            oldFileCount: 93,
            oldFileBytes: 930_000);

        var health = OrganizationHealthCalculator.Evaluate(summary, DuplicateReport.Empty);

        Assert.True(health.Score >= OrganizationHealthCalculator.GoodScore);
        Assert.Equal(HealthBand.Good, health.Band);
    }

    /// <summary>
    /// Age is the weaker of the two signals, so it may move the score but must never carry it.
    /// </summary>
    [Fact]
    public void Evaluate_CountsAgeForLessThanPossibleCopies()
    {
        var health = OrganizationHealthCalculator.Evaluate(
            Summary(1_000_000, [Usage(FileCategory.Documents, 10, 1_000_000)]),
            DuplicateReport.Empty);

        Assert.True(
            Component(health, HealthComponentKind.PossibleCopies).Weight
            > Component(health, HealthComponentKind.UnusedFiles).Weight);
    }

    /// <summary>
    /// A file type DeskAI cannot name is a gap in this app's own knowledge, not a mess the
    /// person made. Scoring it down would blame someone for what DeskAI does not know, so it
    /// is reported as a limit on the reading instead of a penalty.
    /// </summary>
    [Fact]
    public void Evaluate_DoesNotPenaliseFilesItSimplyCannotRecognise()
    {
        var summary = Summary(
            totalBytes: 1_000_000,
            categories:
            [
                Usage(FileCategory.Documents, 5, 500_000),
                Usage(FileCategory.Unknown, 5, 500_000),
            ]);

        var health = OrganizationHealthCalculator.Evaluate(summary, DuplicateReport.Empty);

        Assert.Equal(100, health.Score);
        Assert.Equal(2, health.Components.Count);
    }

    [Fact]
    public void Evaluate_ReportsHowMuchOfTheFolderItCouldRecognise()
    {
        var summary = Summary(
            totalBytes: 1_000_000,
            categories:
            [
                Usage(FileCategory.Documents, 5, 520_000),
                Usage(FileCategory.Unknown, 67, 480_000),
            ]);

        var health = OrganizationHealthCalculator.Evaluate(summary, DuplicateReport.Empty);

        Assert.Equal(0.52, health.RecognisedShare, 3);
        Assert.Equal(67, health.UnrecognisedFileCount);
        Assert.True(health.IsRecognitionPartial);
    }

    [Fact]
    public void Evaluate_DoesNotCallTheReadingPartialWhenItRecognisedNearlyEverything()
    {
        var summary = Summary(
            totalBytes: 1_000_000,
            categories:
            [
                Usage(FileCategory.Documents, 20, 990_000),
                Usage(FileCategory.Unknown, 1, 10_000),
            ]);

        var health = OrganizationHealthCalculator.Evaluate(summary, DuplicateReport.Empty);

        Assert.False(health.IsRecognitionPartial);
    }

    /// <summary>
    /// Every part of the score has to be shown, or the number is just an opinion. Each
    /// component carries what was measured and how much it counted for.
    /// </summary>
    [Fact]
    public void Evaluate_ExplainsEveryComponentOfTheScore()
    {
        var summary = Summary(totalBytes: 1_000_000, categories: [Usage(FileCategory.Documents, 10, 1_000_000)]);

        var health = OrganizationHealthCalculator.Evaluate(summary, DuplicateReport.Empty);

        Assert.Equal(2, health.Components.Count);
        Assert.All(health.Components, component =>
        {
            Assert.InRange(component.Score, 0, 100);
            Assert.True(component.Weight > 0);
            Assert.InRange(component.ShareOfTotal, 0, 1);
        });
        Assert.Equal(100, health.Components.Sum(component => component.Weight));
    }

    /// <summary>
    /// The score is the weighted average of the parts shown, so a person can add it up.
    /// A number that did not follow from its stated components would not be transparent.
    /// </summary>
    [Fact]
    public void Evaluate_ScoreIsTheWeightedAverageOfTheComponentsShown()
    {
        var summary = Summary(
            totalBytes: 1_000_000,
            categories:
            [
                Usage(FileCategory.Documents, 6, 600_000),
                Usage(FileCategory.Unknown, 4, 400_000),
            ],
            oldFileCount: 3,
            oldFileBytes: 300_000);
        var duplicates = Report(reclaimableBytes: 50_000, fileCount: 2);

        var health = OrganizationHealthCalculator.Evaluate(summary, duplicates);

        var expected = (int)Math.Round(
            health.Components.Sum(component => (double)component.Score * component.Weight) / 100,
            MidpointRounding.AwayFromZero);
        Assert.Equal(expected, health.Score);
    }

    [Fact]
    public void Evaluate_KeepsTheScoreInRangeWhenEverythingIsWrongAtOnce()
    {
        var summary = Summary(
            totalBytes: 1_000_000,
            categories: [Usage(FileCategory.Documents, 10, 1_000_000)],
            oldFileCount: 10,
            oldFileBytes: 1_000_000);
        var duplicates = Report(reclaimableBytes: 1_000_000, fileCount: 10);

        var health = OrganizationHealthCalculator.Evaluate(summary, duplicates);

        Assert.Equal(0, health.Score);
        Assert.Equal(HealthBand.NeedsAttention, health.Band);
    }

    /// <summary>
    /// The duplicate stage caps how many groups it examines, so its reclaimable figure can
    /// exceed the total it is compared against only through nonsense input; the share must
    /// still never leave 0-1.
    /// </summary>
    [Fact]
    public void Evaluate_ClampsAShareThatWouldExceedTheTotal()
    {
        var summary = Summary(totalBytes: 100, categories: [Usage(FileCategory.Documents, 2, 100)]);
        var duplicates = Report(reclaimableBytes: 5_000, fileCount: 2);

        var health = OrganizationHealthCalculator.Evaluate(summary, duplicates);

        Assert.Equal(1, Component(health, HealthComponentKind.PossibleCopies).ShareOfTotal);
    }

    private static HealthComponent Component(OrganizationHealth health, HealthComponentKind kind) =>
        Assert.Single(health.Components, component => component.Kind == kind);

    private static CategoryUsage Usage(FileCategory category, int files, long bytes) =>
        new(category, files, bytes);

    private static StorageSummary Summary(
        long totalBytes,
        IReadOnlyList<CategoryUsage> categories,
        int oldFileCount = 0,
        long oldFileBytes = 0) => new(
        FoldersIncluded: 1,
        TotalFiles: categories.Sum(usage => usage.FileCount),
        TotalSizeBytes: totalBytes,
        Categories: categories,
        LargestFiles: [],
        OldFileCount: oldFileCount,
        OldFileBytes: oldFileBytes,
        LastCheckedUtc: null);

    private static DuplicateReport Report(long reclaimableBytes, int fileCount)
    {
        var perFile = reclaimableBytes / Math.Max(1, fileCount - 1);
        var files = Enumerable.Range(0, fileCount)
            .Select(index => new DuplicateCandidate("Study", $"copy-{index}.pdf", $"copy-{index}.pdf"))
            .ToList()
            .AsReadOnly();
        return new DuplicateReport([new DuplicateGroup(perFile, files)], 1, false);
    }
}
