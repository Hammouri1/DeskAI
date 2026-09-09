using DeskAI.Core.Plans;
using DeskAI.Core.Roots;

namespace DeskAI.Safety;

public sealed class PlanValidator(IPathPolicy pathPolicy)
{
    public const string CurrentPolicyVersion = "1";

    public PlanValidationReport Validate(OrganizationPlan plan, AuthorizedRoot root)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(root);

        if (plan.RootId != root.Id)
        {
            return PlanValidationReport.ForWholePlan(
                plan.Id,
                ValidationResult.Blocked(
                    ValidationReasonCode.OutsideAuthorizedRoot,
                    "The plan is not bound to this authorized root."));
        }

        // Asked as a capability rather than compared to one scope. The old comparison
        // blocked exactly MetadataOnly, so any scope added later would have fallen straight
        // through it and become changeable without this line appearing to change.
        if (!RootCapabilities.CanMutate(root))
        {
            return PlanValidationReport.ForWholePlan(
                plan.Id,
                ValidationResult.Blocked(
                    ValidationReasonCode.InvalidOperation,
                    "This folder was not connected for changes, so nothing here can be changed."));
        }

        if (!string.Equals(plan.PolicyVersion, CurrentPolicyVersion, StringComparison.Ordinal))
        {
            return PlanValidationReport.ForWholePlan(
                plan.Id,
                ValidationResult.Blocked(
                    ValidationReasonCode.InvalidOperation,
                    "The plan uses an expired safety policy version."));
        }

        var results = plan.Operations
            .Select(operation => new OperationValidation(operation.Id, ValidateOperation(operation, root)))
            .ToList();

        foreach (var conflict in plan.Issues.Where(issue => issue.Severity == PlanIssueSeverity.Conflict))
        {
            var affectedOperations = conflict.OperationIds.Count == 0 ? [Guid.Empty] : conflict.OperationIds;
            results.AddRange(affectedOperations.Select(operationId => new OperationValidation(
                operationId,
                ValidationResult.Blocked(ValidationReasonCode.Collision, conflict.Explanation))));
        }

        return new PlanValidationReport(plan.Id, results);
    }

    private ValidationResult ValidateOperation(PlanOperation operation, AuthorizedRoot root)
    {
        if (operation.Id == Guid.Empty || string.IsNullOrWhiteSpace(operation.Reason))
        {
            return ValidationResult.Blocked(
                ValidationReasonCode.InvalidOperation,
                "The operation is missing a stable ID or explanation.");
        }

        return operation switch
        {
            CreateDirectoryOperation create => pathPolicy.ValidateRelativePath(root, create.DestinationRelativePath),
            MoveFileOperation move => Combine(
                pathPolicy.ValidateRelativePath(root, move.SourceRelativePath),
                pathPolicy.ValidateRelativePath(root, move.DestinationRelativePath)),
            RenameFileOperation rename => Combine(
                pathPolicy.ValidateRelativePath(root, rename.SourceRelativePath),
                pathPolicy.ValidateRelativePath(root, rename.DestinationRelativePath)),
            _ => ValidationResult.Blocked(
                ValidationReasonCode.InvalidOperation,
                "The operation type is not allowed."),
        };
    }

    private static ValidationResult Combine(ValidationResult source, ValidationResult destination) =>
        source.Status == ValidationStatus.Blocked ? source : destination;
}

public sealed record OperationValidation(Guid OperationId, ValidationResult Result);

public sealed record PlanValidationReport(Guid PlanId, IReadOnlyList<OperationValidation> Operations)
{
    public bool CanBeApproved => Operations.All(item => item.Result.Status != ValidationStatus.Blocked);

    public static PlanValidationReport ForWholePlan(Guid planId, ValidationResult result) =>
        new(planId, [new OperationValidation(Guid.Empty, result)]);
}
