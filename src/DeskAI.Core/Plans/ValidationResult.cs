namespace DeskAI.Core.Plans;

public sealed record ValidationResult(
    ValidationStatus Status,
    ValidationReasonCode ReasonCode,
    string Explanation)
{
    public static ValidationResult Allowed(string explanation = "The operation is within the authorized scope.") =>
        new(ValidationStatus.Allowed, ValidationReasonCode.None, explanation);

    public static ValidationResult Blocked(ValidationReasonCode reasonCode, string explanation) =>
        new(ValidationStatus.Blocked, reasonCode, explanation);
}

public enum ValidationStatus
{
    Allowed,
    Warning,
    Blocked,
}

public enum ValidationReasonCode
{
    None,
    EmptyPath,
    RelativePath,
    UnsupportedPath,
    OutsideAuthorizedRoot,
    ProtectedRoot,
    ProtectedEntry,
    PathTraversal,
    InvalidOperation,
    DuplicateOperationId,
    Collision,
}
