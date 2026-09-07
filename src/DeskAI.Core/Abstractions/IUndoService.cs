using DeskAI.Core.Execution;

namespace DeskAI.Core.Abstractions;

public interface IUndoService
{
    Task<UndoResult> UndoAsync(Guid transactionId, CancellationToken cancellationToken = default);

    Task<int> RecoverIncompleteAsync(CancellationToken cancellationToken = default);
}
