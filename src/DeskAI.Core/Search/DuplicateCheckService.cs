using DeskAI.Core.Abstractions;
using DeskAI.Core.Content;
using DeskAI.Core.Execution;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Search;

/// <summary>One file a duplicate check would read, as DeskAI remembered it.</summary>
public sealed record DuplicateCheckFile(
    Guid RootId,
    string RootName,
    string RelativePath,
    long SizeBytes,
    DateTimeOffset ModifiedAtUtc)
{
    /// <summary>"Folder / path", the way Home names files.</summary>
    public string DisplayName => $"{RootName} / {RelativePath}";
}

/// <summary>
/// Exactly what a duplicate check would read, prepared for the person to see before anything is
/// opened. Comparing reads only these files.
/// </summary>
/// <param name="LeftOut">Files in possible-copy groups beyond the per-check limit.</param>
public sealed record DuplicateCheckQuestion(IReadOnlyList<DuplicateCheckFile> Files, int LeftOut)
{
    public long TotalBytes => Files.Sum(file => file.SizeBytes);

    public int FolderCount => Files.Select(file => file.RootId).Distinct().Count();

    public bool HasAnything => Files.Count > 0;
}

/// <summary>A file a check did not compare, and why, in plain words.</summary>
public sealed record NotComparedFile(DuplicateCheckFile File, string Reason);

/// <summary>What a check found for one group of files that share a size.</summary>
/// <param name="IdenticalSets">Sets of files whose bytes are the same.</param>
/// <param name="Different">Files that match no other file in the group.</param>
public sealed record ComparedGroup(
    long SizeBytes,
    IReadOnlyList<IReadOnlyList<DuplicateCheckFile>> IdenticalSets,
    IReadOnlyList<DuplicateCheckFile> Different,
    IReadOnlyList<NotComparedFile> NotCompared)
{
    public int IdenticalCount => IdenticalSets.Sum(set => set.Count);

    /// <summary>What removing all but one of each identical set would free. Nothing offers to.</summary>
    public long ReclaimableBytes => IdenticalSets.Sum(set => SizeBytes * (set.Count - 1));
}

/// <summary>What one duplicate check found.</summary>
/// <param name="Stopped">The person pressed Stop, so some files were not compared.</param>
public sealed record DuplicateCheckResult(
    IReadOnlyList<ComparedGroup> Groups,
    int FilesOpened,
    long BytesRead,
    bool Stopped)
{
    public int IdenticalCount => Groups.Sum(group => group.IdenticalCount);

    public long ReclaimableBytes => Groups.Sum(group => group.ReclaimableBytes);

    public int NotComparedCount => Groups.Sum(group => group.NotCompared.Count);
}

/// <summary>
/// Tells which possible copies are really identical, by reading them — only after the person
/// has seen exactly which files would be read and agreed.
/// </summary>
/// <remarks>
/// <para>
/// Two calls with the person between them, as with asking AI (ADR 0020).
/// <see cref="PrepareAsync"/> lists the files from Home's size groups and reads nothing;
/// <see cref="CompareAsync"/> reads only files in that list, after checking each folder is still
/// connected and searchable. No permission is stored, so nothing can be read later without
/// asking again.
/// </para>
/// <para>
/// It reads as little as it can: first the beginning of each file, and to the end only files
/// whose beginnings match another's. Bounds are named constants and every file not compared is
/// reported with its reason rather than guessed about. Fingerprints are held in memory for this
/// one check and kept nowhere. Nothing is changed, moved, or deleted, and nothing is offered to be.
/// </para>
/// </remarks>
public sealed class DuplicateCheckService(
    DuplicateFinderService finder,
    IAuthorizedRootRepository roots,
    IFileFingerprinter fingerprinter)
{
    /// <summary>At most this many files are read in one check.</summary>
    public const int MaxFilesPerCheck = 200;

    /// <summary>How much of each file is read first. Files differing here are never read further.</summary>
    public const long BeginningBytes = 64 * 1024;

    /// <summary>A file larger than this is not read to the end.</summary>
    public const long MaxWholeFileBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>At most this much is read to the end in one check.</summary>
    public const long MaxWholeBytesPerCheck = 8L * 1024 * 1024 * 1024;

    /// <summary>Lists what a check would read. Opens no file.</summary>
    public async Task<DuplicateCheckQuestion> PrepareAsync(CancellationToken cancellationToken = default)
    {
        var report = await finder.FindAsync(cancellationToken).ConfigureAwait(false);
        var files = new List<DuplicateCheckFile>();
        var leftOut = 0;

        // Whole groups only: half a group cannot say which of its files are copies.
        foreach (var group in report.Groups)
        {
            if (files.Count + group.Count > MaxFilesPerCheck)
            {
                leftOut += group.Count;
                continue;
            }

            files.AddRange(group.Files.Select(file =>
                new DuplicateCheckFile(file.RootId, file.RootName, file.RelativePath, group.SizeBytes, file.ModifiedAtUtc)));
        }

        return new DuplicateCheckQuestion(files.AsReadOnly(), leftOut);
    }

    /// <summary>Reads the files in the question the person agreed to, and says which are the same.</summary>
    public async Task<DuplicateCheckResult> CompareAsync(
        DuplicateCheckQuestion question,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(question);
        var run = new Run(roots, fingerprinter, cancellationToken);
        var groups = new List<ComparedGroup>();
        foreach (var group in question.Files.GroupBy(file => file.SizeBytes).OrderByDescending(group => group.Key))
        {
            groups.Add(await run.CompareGroupAsync(group.Key, group.ToArray()).ConfigureAwait(false));
        }

        return new DuplicateCheckResult(groups.AsReadOnly(), run.FilesOpened, run.BytesRead, run.Stopped);
    }

    /// <summary>The state of one check: folders already looked up, bytes read, and Stop.</summary>
    private sealed class Run(IAuthorizedRootRepository roots, IFileFingerprinter fingerprinter, CancellationToken cancellationToken)
    {
        private readonly Dictionary<Guid, AuthorizedRoot?> _folders = [];
        private long _wholeBytes;

        public int FilesOpened { get; private set; }
        public long BytesRead { get; private set; }
        public bool Stopped { get; private set; }

        public async Task<ComparedGroup> CompareGroupAsync(long size, DuplicateCheckFile[] files)
        {
            var notCompared = new List<NotComparedFile>();
            var beginnings = await ReadAllAsync(files, BeginningBytes, notCompared).ConfigureAwait(false);

            // A file no longer than the beginning has been read whole already.
            if (size <= BeginningBytes)
            {
                return Sort(size, beginnings, notCompared);
            }

            var identical = new List<IReadOnlyList<DuplicateCheckFile>>();
            var different = new List<DuplicateCheckFile>();
            foreach (var bucket in beginnings.GroupBy(pair => pair.Hash))
            {
                var matching = bucket.Select(pair => pair.File).ToArray();
                if (matching.Length == 1)
                {
                    different.Add(matching[0]);
                    continue;
                }

                if (size > MaxWholeFileBytes)
                {
                    notCompared.AddRange(matching.Select(file => new NotComparedFile(file, "It is over 2 GB, too large to compare this time.")));
                    continue;
                }

                var whole = new List<(DuplicateCheckFile File, string Hash)>();
                foreach (var file in matching)
                {
                    if (_wholeBytes + size > MaxWholeBytesPerCheck)
                    {
                        notCompared.Add(new NotComparedFile(file, "DeskAI stopped at 8 GB read in one check. Check again for the rest."));
                        continue;
                    }

                    _wholeBytes += size;
                    whole.AddRange(await ReadAllAsync([file], null, notCompared).ConfigureAwait(false));
                }

                var sorted = Sort(size, whole, notCompared: []);
                identical.AddRange(sorted.IdenticalSets);
                different.AddRange(sorted.Different);
            }

            return new ComparedGroup(size, identical.AsReadOnly(), different.AsReadOnly(), notCompared.AsReadOnly());
        }

        private static ComparedGroup Sort(long size, List<(DuplicateCheckFile File, string Hash)> read, List<NotComparedFile> notCompared)
        {
            var identical = new List<IReadOnlyList<DuplicateCheckFile>>();
            var different = new List<DuplicateCheckFile>();
            foreach (var bucket in read.GroupBy(pair => pair.Hash))
            {
                var files = bucket.Select(pair => pair.File).ToArray();
                if (files.Length > 1)
                {
                    identical.Add(files);
                }
                else
                {
                    different.Add(files[0]);
                }
            }

            return new ComparedGroup(size, identical.AsReadOnly(), different.AsReadOnly(), notCompared.AsReadOnly());
        }

        private async Task<List<(DuplicateCheckFile File, string Hash)>> ReadAllAsync(
            IEnumerable<DuplicateCheckFile> files,
            long? maxBytes,
            List<NotComparedFile> notCompared)
        {
            var read = new List<(DuplicateCheckFile, string)>();
            foreach (var file in files)
            {
                if (Stopped || cancellationToken.IsCancellationRequested)
                {
                    Stopped = true;
                    notCompared.Add(new NotComparedFile(file, "Stopped before this file."));
                    continue;
                }

                // The folder is checked when its first file is about to be read, not when the
                // question was prepared: it may have been disconnected in between.
                var root = await FolderAsync(file.RootId).ConfigureAwait(false);
                if (root is null)
                {
                    notCompared.Add(new NotComparedFile(file, "That folder is no longer connected, so it was not read."));
                    continue;
                }

                FileFingerprint fingerprint;
                try
                {
                    FilesOpened += maxBytes is null ? 0 : 1;
                    fingerprint = await fingerprinter.FingerprintAsync(
                        root, file.RelativePath, new ExpectedFile(file.SizeBytes, file.ModifiedAtUtc), maxBytes, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Stopped = true;
                    notCompared.Add(new NotComparedFile(file, "Stopped while reading this file."));
                    continue;
                }

                BytesRead += fingerprint.BytesRead;
                if (fingerprint is { WasRead: true, Hash: { } hash })
                {
                    read.Add((file, hash));
                }
                else
                {
                    notCompared.Add(new NotComparedFile(file, fingerprint.Explanation));
                }
            }

            return read;
        }

        private async Task<AuthorizedRoot?> FolderAsync(Guid rootId)
        {
            if (!_folders.TryGetValue(rootId, out var root))
            {
                var found = await roots.FindAsync(rootId, cancellationToken).ConfigureAwait(false);
                root = found is not null && FileSearchService.IsSearchable(found) ? found : null;
                _folders[rootId] = root;
            }

            return root;
        }
    }
}
