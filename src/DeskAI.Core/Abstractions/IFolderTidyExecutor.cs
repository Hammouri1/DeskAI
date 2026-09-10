using DeskAI.Core.Execution;
using DeskAI.Core.Plans;

namespace DeskAI.Core.Abstractions;

/// <summary>
/// Carries out an approved tidy in a folder the person allowed DeskAI to tidy, and undoes it.
/// </summary>
/// <remarks>
/// It trusts the folder only while it may still be tidied, checking again before every file,
/// and it refuses any plan or record belonging to a folder without that permission —
/// including the practice workspace.
/// </remarks>
public interface IFolderTidyExecutor
{
    /// <param name="expected">How each moved file looked when the approved list was made.</param>
    Task<ExecutionResult> ExecuteAsync(
        OrganizationPlan plan,
        Approval approval,
        IReadOnlyDictionary<Guid, ExpectedFile> expected,
        CancellationToken cancellationToken = default);

    /// <exception cref="InvalidOperationException">The record cannot be undone here, with a plain reason.</exception>
    Task<UndoResult> UndoAsync(Guid transactionId, CancellationToken cancellationToken = default);
}
