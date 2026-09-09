using DeskAI.Core.Classification;
using DeskAI.Core.Indexing;

namespace DeskAI.Core.Search;

/// <summary>
/// Turns the storage and possible-copy readings into an explainable health score.
/// </summary>
/// <remarks>
/// <para>
/// This is a pure calculation over figures that have already been gathered. It has no
/// dependencies, performs no I/O, and reaches no folder, so it cannot widen scope: whatever
/// the summary and the duplicate report were allowed to see is all it can ever describe.
/// </para>
/// <para>
/// Each threshold below is a stated judgement rather than a hidden constant, because a
/// score whose reasoning cannot be repeated is not transparent. The UI shows every part,
/// its share, and its weight, so the total can be checked by hand.
/// </para>
/// </remarks>
public static class OrganizationHealthCalculator
{
    /// <summary>
    /// The share of space at which a part scores zero.
    /// </summary>
    /// <remarks>
    /// A part's score falls in a straight line from full marks at nothing to zero at its
    /// limit. The limits differ because the findings differ in weight of meaning: space that
    /// might be duplicated is nearly always waste, so a tenth of a folder is already a lot;
    /// files sitting unchanged are ordinary and only stand out once most of the folder is
    /// like that; an unrecognised type is a mild signal, so a quarter is the point where
    /// DeskAI can barely describe what is there.
    /// </remarks>
    public const double PossibleCopiesLimit = 0.10;

    /// <inheritdoc cref="PossibleCopiesLimit"/>
    public const double UnusedFilesLimit = 0.60;

    /// <inheritdoc cref="PossibleCopiesLimit"/>
    public const double UnrecognisedFilesLimit = 0.25;

    /// <summary>How much each part counts for. The weights add up to one hundred.</summary>
    public const int PossibleCopiesWeight = 40;

    /// <inheritdoc cref="PossibleCopiesWeight"/>
    public const int UnusedFilesWeight = 30;

    /// <inheritdoc cref="PossibleCopiesWeight"/>
    public const int UnrecognisedFilesWeight = 30;

    /// <summary>At or above this the folders look settled.</summary>
    public const int GoodScore = 80;

    /// <summary>At or above this nothing is alarming, but something is worth a look.</summary>
    public const int FairScore = 55;

    public static OrganizationHealth Evaluate(StorageSummary storage, DuplicateReport duplicates)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(duplicates);

        // Nothing connected, or nothing remembered yet, means there is no evidence. Scoring
        // that would be inventing a judgement, so it is reported as not measured instead.
        if (storage.FoldersIncluded == 0 || storage.TotalSizeBytes <= 0)
        {
            return OrganizationHealth.NotMeasured;
        }

        var unrecognised = storage.Categories.FirstOrDefault(usage => usage.Category == FileCategory.Unknown);

        var components = new[]
        {
            Component(
                HealthComponentKind.PossibleCopies,
                duplicates.ReclaimableBytes,
                duplicates.TotalFiles,
                storage.TotalSizeBytes,
                PossibleCopiesLimit,
                PossibleCopiesWeight),
            Component(
                HealthComponentKind.UnusedFiles,
                storage.OldFileBytes,
                storage.OldFileCount,
                storage.TotalSizeBytes,
                UnusedFilesLimit,
                UnusedFilesWeight),
            Component(
                HealthComponentKind.UnrecognisedFiles,
                unrecognised?.TotalSizeBytes ?? 0,
                unrecognised?.FileCount ?? 0,
                storage.TotalSizeBytes,
                UnrecognisedFilesLimit,
                UnrecognisedFilesWeight),
        };

        var totalWeight = components.Sum(component => component.Weight);
        var score = (int)Math.Round(
            components.Sum(component => (double)component.Score * component.Weight) / totalWeight,
            MidpointRounding.AwayFromZero);

        var band = score >= GoodScore ? HealthBand.Good
            : score >= FairScore ? HealthBand.Fair
            : HealthBand.NeedsAttention;

        return new OrganizationHealth(score, band, components.AsReadOnly(), storage.FoldersIncluded);
    }

    private static HealthComponent Component(
        HealthComponentKind kind,
        long measuredBytes,
        int measuredFileCount,
        long totalBytes,
        double limit,
        int weight)
    {
        // A reading larger than the whole can only come from figures gathered a moment
        // apart, so it is clamped rather than allowed to produce a share above one.
        var share = Math.Clamp((double)Math.Max(0, measuredBytes) / totalBytes, 0, 1);
        var partScore = 100 - (int)Math.Round(100 * Math.Min(1, share / limit), MidpointRounding.AwayFromZero);
        return new HealthComponent(kind, measuredBytes, measuredFileCount, share, partScore, weight);
    }
}
