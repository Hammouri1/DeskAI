using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Search;

/// <summary>A bounded image held only while the person reviews this search.</summary>
public sealed record VisualAsset(Guid RootId, string RootName, string RelativePath,
    string FileName, string Location, string MediaType, byte[] Bytes);

public sealed record VisualSearchBatch(string Phrase, AiMode Mode, string ProviderId,
    string ModelId, string? LocalEndpoint, string Destination, string ServiceName, IReadOnlyList<VisualAsset> Images,
    int FilesChecked, bool ReachedLimit)
{
    private int _used;
    internal DateTimeOffset PreparedAtUtc { get; } = DateTimeOffset.UtcNow;

    /// <summary>An approval is for one search attempt, never a reusable upload permit.</summary>
    internal bool TryUse() => Interlocked.Exchange(ref _used, 1) == 0;
}

public sealed record VisualMatch(string Name, string Location, string Explanation);

public sealed record VisualSearchResult(IReadOnlyList<VisualMatch> Matches,
    int ImagesChecked, bool ReachedLimit, string Message);

public interface IVisualAssetReader
{
    Task<IReadOnlyList<VisualAsset>> ReadAsync(AuthorizedRoot root, string relativePath,
        int remainingImages, CancellationToken cancellationToken);
}

public interface IVisualImageMatcher
{
    Task<(bool Matched, string Explanation, string? Error)> MatchAsync(
        VisualAsset image, string phrase, CancellationToken cancellationToken);
}

/// <summary>Searches pixels only after the page has asked for a fresh visual-read choice.</summary>
public sealed class VisualSearchService(
    IAuthorizedRootRepository roots,
    IFileIndex index,
    IAiSettingsRepository settingsRepository,
    IVisualAssetReader reader,
    IVisualImageMatcher matcher)
{
    public const int MaxFiles = 30;
    public const int MaxImages = 12;
    public const int MaxTotalBytes = 4 * 1024 * 1024;

    public async Task<(VisualSearchBatch? Batch, string Message)> PrepareAsync(
        string phrase, Guid? selectedRootId, bool visualReadApproved,
        CancellationToken cancellationToken = default)
    {
        if (!visualReadApproved)
        {
            return (null, "Picture reading was not approved. Nothing was opened.");
        }

        if (string.IsNullOrWhiteSpace(phrase) || phrase.Length > NaturalLanguageQueryTranslator.MaxInputLength)
        {
            return (null, "Describe the picture in a shorter sentence first.");
        }

        var settings = await settingsRepository.LoadAsync(cancellationToken).ConfigureAwait(false);
        var target = AiTarget.Of(settings);
        if (!target.IsSetUp || settings.Mode == AiMode.RuleEngineOnly)
        {
            return (null, "Connect a local AI or set up an online AI in Privacy and AI first.");
        }

        var allowed = (await roots.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(root => RootCapabilities.CanReadMetadata(root)
                && (selectedRootId is null || root.Id == selectedRootId)).ToArray();
        if (allowed.Length == 0)
        {
            return (null, "Connect a folder to search its pictures first.");
        }

        var translated = NaturalLanguageQueryTranslator.Translate(phrase, DateTimeOffset.UtcNow).Query;
        var extensions = translated.Extensions.Where(extension =>
            extension is ".pdf" or ".pptx" or ".jpg" or ".jpeg" or ".png" or ".webp")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var images = new List<VisualAsset>();
        var totalBytes = 0;
        var checkedFiles = 0;
        var limit = false;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        foreach (var root in allowed)
        {
            var files = await index.ListForRootAsync(root.Id, cancellationToken).ConfigureAwait(false);
            foreach (var file in files.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ext = file.Extension;
                if (ext is not (".pdf" or ".pptx" or ".jpg" or ".jpeg" or ".png" or ".webp")
                    || (extensions.Count > 0 && !extensions.Contains(ext)))
                {
                    continue;
                }

                if (checkedFiles >= MaxFiles || images.Count >= MaxImages
                    || totalBytes >= MaxTotalBytes || elapsed.Elapsed >= TimeSpan.FromSeconds(30))
                {
                    limit = true;
                    break;
                }

                checkedFiles++;
                var found = await reader.ReadAsync(root, file.RelativePath,
                    MaxImages - images.Count, cancellationToken).ConfigureAwait(false);
                foreach (var image in found)
                {
                    if (images.Count >= MaxImages || image.Bytes.Length > MaxTotalBytes - totalBytes)
                    {
                        limit = true;
                        break;
                    }

                    images.Add(image);
                    totalBytes += image.Bytes.Length;
                }
            }

            if (limit)
            {
                break;
            }
        }

        if (images.Count == 0)
        {
            return (null, "No readable pictures were found in the first files checked. Refresh the connected folder if you recently added files.");
        }

        return (new VisualSearchBatch(phrase.Trim(), settings.Mode, settings.ProviderId, settings.ModelId,
            settings.Mode == AiMode.Local ? settings.Endpoint : null,
            target.Destination, target.Name, images.AsReadOnly(), checkedFiles, limit),
            $"Ready to inspect {images.Count} picture(s) from {checkedFiles} file(s).");
    }

    public async Task<VisualSearchResult> SearchAsync(
        VisualSearchBatch batch, bool cloudSendApproved, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (!batch.TryUse() || DateTimeOffset.UtcNow - batch.PreparedAtUtc > TimeSpan.FromMinutes(2)
            || batch.Images.Count is < 1 or > MaxImages
            || batch.Images.Sum(image => (long)image.Bytes.Length) > MaxTotalBytes)
        {
            return new([], 0, true, "This picture selection expired or was already used. Start a new picture search.");
        }

        if (batch.Mode == AiMode.Cloud && !cloudSendApproved)
        {
            return new([], 0, batch.ReachedLimit, "Pictures were not sent. You can still search file names and allowed text.");
        }
        var settings = await settingsRepository.LoadAsync(cancellationToken).ConfigureAwait(false);
        var target = AiTarget.Of(settings);
        if (!target.IsSetUp || settings.Mode != batch.Mode || settings.ProviderId != batch.ProviderId
            || settings.ModelId != batch.ModelId
            || (settings.Mode == AiMode.Local && settings.Endpoint != batch.LocalEndpoint)
            || target.Destination != batch.Destination)
        {
            return new([], 0, batch.ReachedLimit,
                "Your AI choice changed while you reviewed the pictures. Nothing was sent.");
        }

        var stillConnected = (await roots.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(RootCapabilities.CanReadMetadata).Select(root => root.Id).ToHashSet();
        if (batch.Images.Any(image => !stillConnected.Contains(image.RootId)))
        {
            return new([], 0, batch.ReachedLimit,
                "A folder was disconnected while you reviewed the pictures. Nothing was sent.");
        }

        var matches = new List<VisualMatch>();
        var checkedImages = 0;
        foreach (var image in batch.Images)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await matcher.MatchAsync(image, batch.Phrase, cancellationToken).ConfigureAwait(false);
            if (result.Error is not null)
            {
                return new(matches.AsReadOnly(), checkedImages, true,
                    $"Stopped after {checkedImages} picture(s): {result.Error}");
            }

            checkedImages++;
            if (result.Matched)
            {
                matches.Add(new VisualMatch(image.FileName, image.Location, result.Explanation));
            }
        }

        return new(matches.AsReadOnly(), checkedImages, batch.ReachedLimit,
            batch.ReachedLimit
                ? $"Checked {checkedImages} pictures. There may be more beyond the search limit."
                : $"Checked {checkedImages} pictures. Search did not change any files.");
    }
}
