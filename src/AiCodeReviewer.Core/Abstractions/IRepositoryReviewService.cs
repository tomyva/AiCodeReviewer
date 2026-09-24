using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Core.Abstractions;

public interface IRepositoryReviewService
{
    Task<RepositoryReviewResult> ReviewAsync(
        string repositoryPath,
        RepositoryReviewOptions options,
        CancellationToken cancellationToken = default);
}
