using DeskAI.Core.Files;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Abstractions;

public interface IFileScanner
{
    IAsyncEnumerable<ScanEvent> ScanAsync(
        AuthorizedRoot root,
        MetadataScanOptions options,
        CancellationToken cancellationToken = default);
}
