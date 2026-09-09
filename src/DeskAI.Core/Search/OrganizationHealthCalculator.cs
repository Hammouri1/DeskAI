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
    /// limit. Both limits are deliberately generous. Matching sizes are unconfirmed
    /// evidence, so a fifth of a folder is the point at which duplication is worth
    /// mentioning at all; scoring hard on a signal DeskAI has not proven would overstate
    /// what it knows.
    /// </remarks>
    public const double PossibleCopiesLimit = 0.20;

    /// <summary>
    /// Age only reaches zero when effectively the whole folder is untouched.
    /// </summary>
    /// <remarks>
    /// A settled archive is nearly all old by definition and is not a mess. An earlier,
    /// harsher limit flagged exactly that case, which would have made the score say that
    /// moving more files is always better. `UI-UX.md` forbids it saying that.
    /// </remarks>
    public const double UnusedFilesLimit = 1.00;

    /// <summary>
    /// How much each part counts for. The weights add up to one hundred.
    /// </summary>
    /// <remarks>
    /// Possible copies carries most of the score because it is the one finding metadata
    /// alone genuinely supports: two files of the same exact size are worth a look. Age is
    /// weak evidence of disorder, so it nudges the score rather than deciding it.
    /// </remarks>
    public const int PossibleCopiesWeight = 80;

    /// <inheritdoc cref="PossibleCopiesWeight"/>
    public const int UnusedFilesWeight = 20;

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
        };

        var totalWeight = components.Sum(component => component.Weight);
        var score = (int)Math.Round(
            components.Sum(component => (double)component.Score * component.Weight) / totalWeight,
            MidpointRounding.AwayFromZero);

        var band = score >= GoodScore ? HealthBand.Good
            : score >= FairScore ? HealthBand.Fair
            : HealthBand.NeedsAttention;

        // Files DeskAI could not name are reported beside the score, never inside it. An
        // unrecognised type is a gap in what this app has learned, not a mess someone made,
        // so it limits how much of the folder the reading covers instead of lowering it.
        var unrecognised = storage.Categories.FirstOrDefault(usage => usage.Category == FileCategory.Unknown);
        var unrecognisedBytes = Math.Clamp(unrecognised?.TotalSizeBytes ?? 0, 0, storage.TotalSizeBytes);
        var recognisedShare = 1 - ((double)unrecognisedBytes / storage.TotalSizeBytes);

        return new OrganizationHealth(
            score,
            band,
            components.AsReadOnly(),
            recognisedShare,
            unrecognised?.FileCount ?? 0,
            storage.FoldersIncluded);
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
