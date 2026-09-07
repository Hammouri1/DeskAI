using DeskAI.Core.Abstractions;
using DeskAI.Core.Execution;
using DeskAI.Core.Plans;
using DeskAI.Core.Roots;
using DeskAI.Safety;
using Microsoft.Extensions.Options;

namespace DeskAI.Infrastructure.Execution;

/// <summary>
/// Executes only inside a generated, marker-protected directory below the system temp root.
/// It deliberately has no API for accepting an arbitrary user path.
/// </summary>
public sealed class TemporaryDemoPlanExecutor : IPlanExecutor
{
    private const string MarkerName = ".deskai-demo-root";
    private const string OwnedPrefix = "DeskAI.Demo.";
    private readonly string _canonicalTempRoot;
    private readonly string _basePath;
    private readonly string _markerToken = Guid.NewGuid().ToString("N");
    private readonly PlanValidator _validator;
    private readonly IClock _clock;
    private bool _prepared;

    public TemporaryDemoPlanExecutor(
        IOptions<DemoWorkspaceOptions> options,
        PlanValidator validator,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _canonicalTempRoot = Normalize(Path.GetTempPath());
        _basePath = Normalize(options.Value.BasePath);
        EnsureContained(_canonicalTempRoot, _basePath, "Demo base must stay inside the system temporary directory.");

        var rootPath = Path.Combine(_basePath, OwnedPrefix + Guid.NewGuid().ToString("N"));
        Root = AuthorizedRoot.Create(Guid.NewGuid(), rootPath, "Safe temporary demo", RootAccessLevel.Allowed);
    }

    public AuthorizedRoot Root { get; }
    public bool IsPrepared => _prepared;

    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        if (_prepared)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        VerifyRootShape();
        RejectReparsePointsInExistingPath(_canonicalTempRoot, _basePath);
        Directory.CreateDirectory(_basePath);
        RejectReparsePointsInExistingPath(_canonicalTempRoot, _basePath);
        if (Directory.Exists(Root.CanonicalPath) || File.Exists(Root.CanonicalPath))
        {
            throw new InvalidOperationException("The unique temporary demo path already exists.");
        }

        Directory.CreateDirectory(Root.CanonicalPath);

        await File.WriteAllTextAsync(Path.Combine(Root.CanonicalPath, MarkerName), _markerToken, cancellationToken)
            .ConfigureAwait(false);
        await WriteDummyAsync(@"Inbox\course-notes.pdf", "Generated course notes A", cancellationToken).ConfigureAwait(false);
        await WriteDummyAsync(@"Archive\course-notes.pdf", "Generated course notes B", cancellationToken).ConfigureAwait(false);
        await WriteDummyAsync("Screenshot 2026-09-07.png", "Generated screenshot placeholder", cancellationToken).ConfigureAwait(false);
        await WriteDummyAsync("semester-budget.xlsx", "Generated spreadsheet placeholder", cancellationToken).ConfigureAwait(false);
        await WriteDummyAsync(@"Documents\reading-list.md", "Generated reading list", cancellationToken).ConfigureAwait(false);
        await WriteDummyAsync("unrecognized.deskai-demo", "Generated unknown placeholder", cancellationToken).ConfigureAwait(false);
        _prepared = true;
        VerifyOwnedRoot();
    }

    public async Task<ExecutionResult> ExecuteAsync(
        OrganizationPlan plan,
        Approval approval,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(approval);
        var started = _clock.UtcNow;
        var results = new List<OperationExecutionResult>();
        var selected = plan.Operations.Where(operation => approval.SelectedOperationIds.Contains(operation.Id)).ToArray();

        var refusal = ValidateEnvelope(plan, approval, selected);
        if (refusal is not null)
        {
            results.AddRange(selected.Select(operation => Failed(operation.Id, refusal)));
            return Finish(plan.Id, started, results);
        }

        foreach (var operation in selected)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                results.Add(new OperationExecutionResult(operation.Id, ExecutionOutcome.Cancelled, "Cancelled before this operation started."));
                break;
            }

            try
            {
                VerifyOwnedRoot();
                ExecuteOperation(operation);
                results.Add(new OperationExecutionResult(operation.Id, ExecutionOutcome.Completed, null));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                results.Add(Failed(operation.Id, exception.Message));
            }

            await Task.Yield();
        }

        return Finish(plan.Id, started, results);
    }

    private string? ValidateEnvelope(
        OrganizationPlan plan,
        Approval approval,
        PlanOperation[] selected)
    {
        if (!_prepared)
        {
            return "The temporary demo workspace was not prepared.";
        }

        if (plan.RootId != Root.Id || approval.PlanId != plan.Id || approval.PlanRevision != plan.Revision ||
            !string.Equals(approval.PolicyVersion, plan.PolicyVersion, StringComparison.Ordinal))
        {
            return "The approval does not match this exact plan revision and policy.";
        }

        var knownIds = plan.Operations.Select(operation => operation.Id).ToHashSet();
        if (approval.Id == Guid.Empty || !approval.SelectedOperationIds.IsSubsetOf(knownIds))
        {
            return "The approval contains an unknown operation or invalid identity.";
        }

        if (selected.Length == 0)
        {
            return "No operations were selected.";
        }

        var report = _validator.Validate(plan, Root);
        foreach (var operation in selected)
        {
            if (report.Operations.Any(item => item.OperationId == operation.Id && item.Result.Status == ValidationStatus.Blocked))
            {
                return "A selected operation is blocked by the current safety policy.";
            }
        }

        return null;
    }

    private void ExecuteOperation(PlanOperation operation)
    {
        switch (operation)
        {
            case CreateDirectoryOperation create:
                CreateDirectory(create.DestinationRelativePath);
                break;
            case MoveFileOperation move:
                MoveFile(move.SourceRelativePath, move.DestinationRelativePath);
                break;
            case RenameFileOperation rename:
                MoveFile(rename.SourceRelativePath, rename.DestinationRelativePath);
                break;
            default:
                throw new InvalidOperationException("The operation type is not supported by the demo executor.");
        }
    }

    private void CreateDirectory(string relativePath)
    {
        var destination = Resolve(relativePath);
        var parent = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("The destination has no parent.");
        RejectReparsePointsInExistingPath(Root.CanonicalPath, parent);
        if (!Directory.Exists(parent))
        {
            throw new InvalidOperationException("The parent folder was not selected for creation first.");
        }

        if (File.Exists(destination))
        {
            throw new IOException("A file already occupies the requested folder path.");
        }

        if (!Directory.Exists(destination))
        {
            Directory.CreateDirectory(destination);
        }
    }

    private void MoveFile(string sourceRelativePath, string destinationRelativePath)
    {
        var source = Resolve(sourceRelativePath);
        var destination = Resolve(destinationRelativePath);
        var parent = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("The destination has no parent.");
        RejectReparsePointsInExistingPath(Root.CanonicalPath, source);
        RejectReparsePointsInExistingPath(Root.CanonicalPath, parent);

        if (!File.Exists(source) || Directory.Exists(source))
        {
            throw new IOException("The source file is missing or is not a regular file.");
        }

        if (!Directory.Exists(parent))
        {
            throw new IOException("The destination folder does not exist.");
        }

        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new IOException("The destination already exists; overwrite was refused.");
        }

        File.Move(source, destination);
    }

    private string Resolve(string relativePath)
    {
        var policyResult = new WindowsPathPolicy().ValidateRelativePath(Root, relativePath);
        if (policyResult.Status == ValidationStatus.Blocked)
        {
            throw new InvalidOperationException(policyResult.Explanation);
        }

        var resolved = Path.GetFullPath(relativePath, Root.CanonicalPath);
        EnsureContained(Root.CanonicalPath, resolved, "The operation escaped the temporary demo root.");
        return resolved;
    }

    private async Task WriteDummyAsync(string relativePath, string content, CancellationToken cancellationToken)
    {
        VerifyOwnedRoot();
        var destination = Resolve(relativePath);
        var parent = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("Dummy file path has no parent.");
        RejectReparsePointsInExistingPath(Root.CanonicalPath, parent);
        Directory.CreateDirectory(parent);
        RejectReparsePointsInExistingPath(Root.CanonicalPath, parent);
        await File.WriteAllTextAsync(destination, content, cancellationToken).ConfigureAwait(false);
    }

    private void VerifyOwnedRoot()
    {
        VerifyRootShape();
        RejectReparsePointsInExistingPath(_canonicalTempRoot, Root.CanonicalPath);
        var marker = Path.Combine(Root.CanonicalPath, MarkerName);
        if (!File.Exists(marker) || File.GetAttributes(marker).HasFlag(FileAttributes.ReparsePoint) ||
            !string.Equals(File.ReadAllText(marker), _markerToken, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The temporary demo ownership marker is missing or invalid.");
        }
    }

    private void VerifyRootShape()
    {
        EnsureContained(_basePath, Root.CanonicalPath, "The demo root escaped its dedicated base directory.");
        if (!Path.GetFileName(Root.CanonicalPath).StartsWith(OwnedPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The demo root does not have the required owned name.");
        }
    }

    private static void RejectReparsePointsInExistingPath(string ancestor, string candidate)
    {
        EnsureContained(ancestor, candidate, "The checked path escaped its expected ancestor.");
        var relative = Path.GetRelativePath(ancestor, candidate);
        var current = ancestor;
        if (Directory.Exists(current) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException("A reparse point is not allowed in the demo path.");
        }

        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((Directory.Exists(current) || File.Exists(current)) &&
                File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException("A reparse point is not allowed in the demo path.");
            }
        }
    }

    private static void EnsureContained(string ancestor, string candidate, string message)
    {
        var relative = Path.GetRelativePath(Normalize(ancestor), Normalize(candidate));
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private ExecutionResult Finish(Guid planId, DateTimeOffset started, IReadOnlyList<OperationExecutionResult> results) =>
        new(Guid.NewGuid(), planId, results, started, _clock.UtcNow);

    private static OperationExecutionResult Failed(Guid operationId, string error) =>
        new(operationId, ExecutionOutcome.Failed, error);

    private static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
