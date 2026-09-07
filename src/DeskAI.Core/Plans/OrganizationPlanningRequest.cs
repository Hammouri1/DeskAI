using DeskAI.Core.Classification;
using DeskAI.Core.Files;
using DeskAI.Core.Recipes;
using FileClassification = DeskAI.Core.Classification.Classification;

namespace DeskAI.Core.Plans;

public sealed record ClassifiedFile
{
    public ClassifiedFile(FileItem file, FileClassification classification)
    {
        File = file ?? throw new ArgumentNullException(nameof(file));
        Classification = classification ?? throw new ArgumentNullException(nameof(classification));
    }

    public FileItem File { get; }
    public FileClassification Classification { get; }
}

public sealed record OrganizationPlanningRequest
{
    public OrganizationPlanningRequest(
        Guid planId,
        Guid rootId,
        int revision,
        DateTimeOffset createdAtUtc,
        string policyVersion,
        FolderRecipe recipe,
        IEnumerable<ClassifiedFile> files)
    {
        if (planId == Guid.Empty || rootId == Guid.Empty)
        {
            throw new ArgumentException("Plan and root IDs must be non-empty.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(revision, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyVersion);
        Recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
        ArgumentNullException.ThrowIfNull(files);

        var materialized = files.ToArray();
        if (materialized.Select(item => item.File.Id).Distinct().Count() != materialized.Length)
        {
            throw new ArgumentException("Each planned file must have a unique ID.", nameof(files));
        }

        if (materialized
            .Select(item => item.File.RelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() != materialized.Length)
        {
            throw new ArgumentException("Each planned file must have a unique relative path.", nameof(files));
        }

        PlanId = planId;
        RootId = rootId;
        Revision = revision;
        CreatedAtUtc = createdAtUtc;
        PolicyVersion = policyVersion;
        Files = Array.AsReadOnly(materialized);
    }

    public Guid PlanId { get; }
    public Guid RootId { get; }
    public int Revision { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public string PolicyVersion { get; }
    public FolderRecipe Recipe { get; }
    public IReadOnlyList<ClassifiedFile> Files { get; }
}
