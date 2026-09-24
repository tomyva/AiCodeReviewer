using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Core.Abstractions;

public interface ISpecialistCodeReviewService : ICodeReviewService
{
    Task<CodeReviewResult> ReviewAsSpecialistAsync(
        SourceFile sourceFile,
        SpecialistProfile specialist,
        CancellationToken cancellationToken = default);
}
