using System.Security.Cryptography;
using System.Text;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Classification;

namespace DeskAI.Core.Plans;

public sealed class OrganizationPlanner : IOrganizationPlanner
{
    public OrganizationPlan CreatePlan(OrganizationPlanningRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var issues = new List<PlanIssue>();
        var moves = new List<MoveFileOperation>();

        foreach (var item in request.Files.OrderBy(item => item.File.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var classification = item.Classification;
            if (classification.Category == FileCategory.Unknown)
            {
                issues.Add(Issue(
                    PlanIssueCode.UnclassifiedFile,
                    PlanIssueSeverity.Information,
                    "No deterministic classification is available, so no move is proposed.",
                    [item.File.Id],
                    []));
                continue;
            }

            var destinationDirectory = request.Recipe.FindDestination(classification.Category);
            if (destinationDirectory is null)
            {
                issues.Add(Issue(
                    PlanIssueCode.NoRecipeDestination,
                    PlanIssueSeverity.Information,
                    "The selected recipe has no destination for this category.",
                    [item.File.Id],
                    []));
                continue;
            }

            var destination = NormalizeRelativePath(Path.Combine(
                destinationDirectory,
                Path.GetFileName(item.File.RelativePath)));
            if (string.Equals(destination, item.File.RelativePath, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(Issue(
                    PlanIssueCode.AlreadyOrganized,
                    PlanIssueSeverity.Information,
                    "The file is already at the recipe destination.",
                    [item.File.Id],
                    []));
                continue;
            }

            var operationId = CreateStableId(
                request.PlanId,
                $"move:{item.File.Id:N}:{item.File.RelativePath}:{destination}");
            moves.Add(new MoveFileOperation(
                operationId,
                item.File.RelativePath,
                destination,
                $"{classification.Reason}; mapped by {request.Recipe.DisplayName} recipe",
                MapProvenance(classification.Source)));
        }

        var directories = CreateDirectoryOperations(request, moves);
        DetectConflicts(request.Files, directories, moves, issues);

        var operations = directories
            .Cast<PlanOperation>()
            .Concat(moves)
            .ToArray();

        return OrganizationPlan.CreateDraft(
            request.PlanId,
            request.RootId,
            request.Revision,
            request.CreatedAtUtc,
            request.PolicyVersion,
            operations,
            issues);
    }

    private static CreateDirectoryOperation[] CreateDirectoryOperations(
        OrganizationPlanningRequest request,
        IEnumerable<MoveFileOperation> moves)
    {
        var paths = moves
            .Select(move => Path.GetDirectoryName(move.DestinationRelativePath))
            .OfType<string>()
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .SelectMany(ExpandDirectories)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path.Count(character => character == Path.DirectorySeparatorChar))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase);

        return paths
            .Select(path => new CreateDirectoryOperation(
                CreateStableId(request.PlanId, $"directory:{path}"),
                path,
                $"Required by {request.Recipe.DisplayName} recipe",
                OperationProvenance.Rule))
            .ToArray();
    }

    private static IEnumerable<string> ExpandDirectories(string destination)
    {
        var segments = destination.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = string.Empty;
        foreach (var segment in segments)
        {
            current = string.IsNullOrEmpty(current) ? segment : Path.Combine(current, segment);
            yield return current;
        }
    }

    private static void DetectConflicts(
        IReadOnlyList<ClassifiedFile> files,
        IReadOnlyList<CreateDirectoryOperation> directories,
        IReadOnlyList<MoveFileOperation> moves,
        List<PlanIssue> issues)
    {
        var fileByPath = files.ToDictionary(item => item.File.RelativePath, StringComparer.OrdinalIgnoreCase);

        foreach (var group in moves.GroupBy(move => move.DestinationRelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var groupedMoves = group.ToArray();
            if (groupedMoves.Length > 1)
            {
                issues.Add(Issue(
                    PlanIssueCode.DuplicateDestination,
                    PlanIssueSeverity.Conflict,
                    "Multiple files would be moved to the same destination.",
                    groupedMoves.Select(move => FindFileId(files, move.SourceRelativePath)).ToArray(),
                    groupedMoves.Select(move => move.Id).ToArray()));
            }
        }

        foreach (var move in moves)
        {
            if (fileByPath.TryGetValue(move.DestinationRelativePath, out var occupant) &&
                !string.Equals(occupant.File.RelativePath, move.SourceRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(Issue(
                    PlanIssueCode.DestinationOccupiedByFile,
                    PlanIssueSeverity.Conflict,
                    "The destination is occupied by another scanned file.",
                    [FindFileId(files, move.SourceRelativePath), occupant.File.Id],
                    [move.Id]));
            }
        }

        foreach (var directory in directories)
        {
            if (fileByPath.TryGetValue(directory.DestinationRelativePath, out var occupant))
            {
                issues.Add(Issue(
                    PlanIssueCode.DirectoryPathOccupiedByFile,
                    PlanIssueSeverity.Conflict,
                    "A required directory path is occupied by a scanned file.",
                    [occupant.File.Id],
                    [directory.Id]));
            }
        }
    }

    private static Guid FindFileId(IEnumerable<ClassifiedFile> files, string sourceRelativePath) =>
        files.Single(item => string.Equals(
            item.File.RelativePath,
            sourceRelativePath,
            StringComparison.OrdinalIgnoreCase)).File.Id;

    private static PlanIssue Issue(
        PlanIssueCode code,
        PlanIssueSeverity severity,
        string explanation,
        IReadOnlyList<Guid> fileIds,
        IReadOnlyList<Guid> operationIds) =>
        new(code, severity, explanation, fileIds, operationIds);

    private static string NormalizeRelativePath(string path) =>
        path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static Guid CreateStableId(Guid planId, string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{planId:N}:{value.ToUpperInvariant()}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static OperationProvenance MapProvenance(ClassificationSource source) => source switch
    {
        ClassificationSource.Rule => OperationProvenance.Rule,
        ClassificationSource.Heuristic => OperationProvenance.Heuristic,
        ClassificationSource.LocalAi => OperationProvenance.LocalAi,
        ClassificationSource.CloudAi => OperationProvenance.CloudAi,
        ClassificationSource.User => OperationProvenance.User,
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };
}
