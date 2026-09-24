using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Core.Abstractions;

public interface IEmbeddingService
{
    Task<EmbeddingBatch> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default);
}
