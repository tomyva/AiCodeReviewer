namespace AiCodeReviewer.Core.Models;

public sealed record RepositoryReviewResult(
    IReadOnlyList<ReviewFinding> Findings,
    RepositoryReviewStatistics Statistics,
    IReadOnlyList<RepositoryFileFailure> Failures,
    ApiUsage? Usage,
    int? EmbeddingTokens,
    string Model);
