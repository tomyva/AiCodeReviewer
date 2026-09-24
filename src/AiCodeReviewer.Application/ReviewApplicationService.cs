using AiCodeReviewer.Core.Exceptions;

namespace AiCodeReviewer.Application;

public sealed class ReviewApplicationService : ICodeReviewerApplication
{
    private readonly IReviewWorkflowDispatcher _dispatcher;
    private readonly IUserStateStore? _userState;

    public ReviewApplicationService(IReviewWorkflowDispatcher dispatcher, IUserStateStore? userState = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _userState = userState;
    }

    public ConfigurationStatus GetConfigurationStatus() => ConfigurationInspector.Inspect();

    public async Task<ApplicationResult<ReviewReport>> ReviewAsync(
        ReviewRequest request,
        IProgress<ReviewProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            progress?.Report(new ReviewProgress(ReviewStage.Validating, "Validating review request", 0));
            Validate(request);
            var report = await _dispatcher.ReviewAsync(request, progress, cancellationToken);
            _userState?.AddRecentReview(new RecentReviewEntry(Guid.NewGuid(), report.CompletedAt, report.Mode, report.Findings.Count, null));
            progress?.Report(new ReviewProgress(ReviewStage.Completed, "Review completed", 100));
            return ApplicationResult.Success(report);
        }
        catch (OperationCanceledException)
        {
            return ApplicationResult.Failure<ReviewReport>(ApplicationErrorCode.Cancelled, "The review was cancelled.");
        }
        catch (SourceFileException exception)
        {
            return ApplicationResult.Failure<ReviewReport>(ApplicationErrorCode.File, exception.Message);
        }
        catch (RepositoryException exception)
        {
            return ApplicationResult.Failure<ReviewReport>(ApplicationErrorCode.Repository, exception.Message);
        }
        catch (CodeReviewServiceException exception)
        {
            return ApplicationResult.Failure<ReviewReport>(ApplicationErrorCode.Provider, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return ApplicationResult.Failure<ReviewReport>(ApplicationErrorCode.Validation, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return ApplicationResult.Failure<ReviewReport>(ApplicationErrorCode.Configuration, exception.Message);
        }
        catch (Exception)
        {
            return ApplicationResult.Failure<ReviewReport>(
                ApplicationErrorCode.Unexpected,
                "An unexpected error occurred. Review the application logs for details.");
        }
    }

    public async Task<ApplicationResult<bool>> PublishPullRequestReviewAsync(
        PublishReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!request.HumanApproved)
            {
                throw new ArgumentException("Fresh human confirmation is required before publishing.");
            }

            await _dispatcher.PublishAsync(request, cancellationToken);
            return ApplicationResult.Success(true);
        }
        catch (OperationCanceledException)
        {
            return ApplicationResult.Failure<bool>(ApplicationErrorCode.Cancelled, "Publishing was cancelled.");
        }
        catch (RepositoryException exception)
        {
            return ApplicationResult.Failure<bool>(ApplicationErrorCode.Repository, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return ApplicationResult.Failure<bool>(ApplicationErrorCode.Validation, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return ApplicationResult.Failure<bool>(ApplicationErrorCode.Configuration, exception.Message);
        }
    }

    public string ExportJson(ReviewReport report) => ReviewReportExporter.ToJson(report);

    public string ExportMarkdown(ReviewReport report) => ReviewReportExporter.ToMarkdown(report);

    public static void Validate(ReviewRequest request)
    {
        if (request.MaximumFiles <= 0 || request.MaximumCharacters <= 0)
        {
            throw new ArgumentException("Repository limits must be positive integers.");
        }

        switch (request.Mode)
        {
            case ReviewMode.File or ReviewMode.Specialists when string.IsNullOrWhiteSpace(request.Path):
                throw new ArgumentException("A source-file path is required.");
            case ReviewMode.LocalRepository or ReviewMode.RagRepository or ReviewMode.AgentRepository
                when string.IsNullOrWhiteSpace(request.Path):
                throw new ArgumentException("A local repository path is required.");
            case ReviewMode.RemoteRepository when string.IsNullOrWhiteSpace(request.RemoteUrl):
                throw new ArgumentException("A remote HTTPS Git URL is required.");
            case ReviewMode.GitHubPullRequest when
                string.IsNullOrWhiteSpace(request.GitHubOwner) ||
                string.IsNullOrWhiteSpace(request.GitHubRepository) ||
                request.PullRequestNumber <= 0:
                throw new ArgumentException("GitHub owner, repository, and a positive pull-request number are required.");
        }
    }
}
