namespace AiCodeReviewer.Application;

public interface ICodeReviewerApplication
{
    ConfigurationStatus GetConfigurationStatus();

    Task<ApplicationResult<ReviewReport>> ReviewAsync(
        ReviewRequest request,
        IProgress<ReviewProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<ApplicationResult<bool>> PublishPullRequestReviewAsync(
        PublishReviewRequest request,
        CancellationToken cancellationToken = default);

    string ExportJson(ReviewReport report);

    string ExportMarkdown(ReviewReport report);
}

public interface IReviewWorkflowDispatcher
{
    Task<ReviewReport> ReviewAsync(
        ReviewRequest request,
        IProgress<ReviewProgress>? progress,
        CancellationToken cancellationToken);

    Task PublishAsync(PublishReviewRequest request, CancellationToken cancellationToken);
}
