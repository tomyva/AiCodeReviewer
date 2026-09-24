using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Core.Abstractions;

public interface IRetrievalAwareCodeReviewService : ICodeReviewService
{
    Task<CodeReviewResult> ReviewWithContextAsync(
        SourceFile sourceFile,
        IReadOnlyList<CodeChunk> relatedContext,
        CancellationToken cancellationToken = default);
}
