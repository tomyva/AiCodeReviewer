using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Core.Abstractions;

public interface ICodeReviewService
{
    Task<CodeReviewResult> ReviewAsync(SourceFile sourceFile, CancellationToken cancellationToken = default);
}
