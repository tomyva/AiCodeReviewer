using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Core.Abstractions;

public interface IAgentReviewService
{
    Task<AgentReviewResult> ReviewRepositoryAsync(
        string repositoryPath,
        AgentReviewOptions options,
        CancellationToken cancellationToken = default);
}
