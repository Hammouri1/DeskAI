using DeskAI.Core.Files;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Ai;

public static class AiRequestBuilder
{
    public static OrganizationSuggestionRequest Build(
        AuthorizedRoot root,
        IEnumerable<FileItem> files,
        IReadOnlySet<Guid> protectedFileIds,
        IReadOnlySet<DisclosureCategory> allowedDisclosures,
        AiRequestLimits limits)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(protectedFileIds);
        ArgumentNullException.ThrowIfNull(allowedDisclosures);
        ArgumentNullException.ThrowIfNull(limits);

        if (limits.MaximumFiles < 1 || limits.MaximumRequestBytes < 1 || limits.MaximumResponseBytes < 1 ||
            limits.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "AI request limits must be positive.");
        }

        var protectedCount = 0;
        var candidates = new List<AiFileCandidate>();
        foreach (var file in files.OrderBy(file => file.Id))
        {
            if (protectedFileIds.Contains(file.Id))
            {
                protectedCount++;
                continue;
            }

            if (candidates.Count == limits.MaximumFiles)
            {
                break;
            }

            var relativeFolder = Path.GetDirectoryName(file.RelativePath);
            candidates.Add(new AiFileCandidate(
                file.Id,
                Allowed(DisclosureCategory.Extension) ? Path.GetExtension(file.RelativePath) : null,
                Allowed(DisclosureCategory.Metadata) ? file.SizeBytes : null,
                Allowed(DisclosureCategory.Metadata) ? file.ModifiedAtUtc : null,
                Allowed(DisclosureCategory.FileName) ? Path.GetFileName(file.RelativePath) : null,
                Allowed(DisclosureCategory.FolderNames) && !string.IsNullOrWhiteSpace(relativeFolder) ? relativeFolder : null,
                Allowed(DisclosureCategory.FullPath) ? Path.GetFullPath(file.RelativePath, root.CanonicalPath) : null));
        }

        return new OrganizationSuggestionRequest(
            OrganizationSuggestionRequest.CurrentSchemaVersion,
            Guid.NewGuid(),
            candidates.AsReadOnly(),
            new DisclosureSummary(new HashSet<DisclosureCategory>(allowedDisclosures), candidates.Count, protectedCount),
            limits);

        bool Allowed(DisclosureCategory category) => allowedDisclosures.Contains(category);
    }
}
