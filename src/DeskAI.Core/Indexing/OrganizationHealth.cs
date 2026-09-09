namespace DeskAI.Core.Indexing;

/// <summary>Which part of the picture a health component measures.</summary>
/// <remarks>
/// A component is identified rather than described here so that Core stays free of user
/// wording: the UI decides how each part is named and explained to a person.
/// </remarks>
public enum HealthComponentKind
{
    /// <summary>How much space might be taken by copies of the same file.</summary>
    PossibleCopies,

    /// <summary>How much space has not changed in a long time.</summary>
    UnusedFiles,
}

/// <summary>How settled the connected folders look overall.</summary>
public enum HealthBand
{
    /// <summary>There was nothing to measure, so no judgement was made.</summary>
    NotMeasured,
    NeedsAttention,
    Fair,
    Good,
}

/// <summary>
/// One measured part of the health score, with the evidence behind it.
/// </summary>
/// <remarks>
/// Every field is carried so the score can be shown as a sum a person can check rather than
/// as an opinion: what was measured, how big a share of the whole it is, the part score it
/// produced, and how much that part counted for.
/// </remarks>
public sealed record HealthComponent(
    HealthComponentKind Kind,
    long MeasuredBytes,
    int MeasuredFileCount,
    double ShareOfTotal,
    int Score,
    int Weight);

/// <summary>
/// A transparent reading of how organized the connected folders look.
/// </summary>
/// <remarks>
/// <para>
/// This is derived arithmetic over remembered metadata. It opens no file, reads nothing new
/// from disk, and involves no AI: the same numbers always produce the same score, which is
/// what makes it explainable.
/// </para>
/// <para>
/// The score describes and never proposes. It produces no plan and cannot start an
/// operation, and there is deliberately no "fix this" action attached to it: any cleanup a
/// person chooses still goes through the ordinary preview and approval path.
/// </para>
/// </remarks>
public sealed record OrganizationHealth(
    int Score,
    HealthBand Band,
    IReadOnlyList<HealthComponent> Components,
    double RecognisedShare,
    int UnrecognisedFileCount,
    int FoldersIncluded)
{
    /// <summary>Above this the reading is complete enough not to need a caveat.</summary>
    /// <remarks>
    /// A line announcing that DeskAI recognised 99% of a folder is noise. The caveat is for
    /// the case where a real part of the folder is invisible to the reading.
    /// </remarks>
    public const double FullRecognitionShare = 0.95;

    public static OrganizationHealth NotMeasured { get; } = new(0, HealthBand.NotMeasured, [], 1, 0, 0);

    public bool IsMeasured => Band != HealthBand.NotMeasured;

    /// <summary>
    /// True when DeskAI could not tell the type of enough of the folder to call the reading
    /// complete.
    /// </summary>
    /// <remarks>
    /// This is a limit on what DeskAI knows, not a fault in the folder, which is why it is
    /// carried separately from the score instead of lowering it. Blaming a person for the
    /// file types this app has not learned would be both unfair and uninformative.
    /// </remarks>
    public bool IsRecognitionPartial => IsMeasured && RecognisedShare < FullRecognitionShare;
}
