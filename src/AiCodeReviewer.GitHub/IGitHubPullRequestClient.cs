namespace AiCodeReviewer.GitHub;

public interface IGitHubPullRequestClient
{
    Task<PullRequestData> GetAsync(PullRequestReference reference, CancellationToken cancellationToken = default);

    Task PostReviewAsync(
        PullRequestData pullRequest,
        IReadOnlyList<DraftReviewComment> comments,
        bool humanApproved,
        CancellationToken cancellationToken = default);
}
