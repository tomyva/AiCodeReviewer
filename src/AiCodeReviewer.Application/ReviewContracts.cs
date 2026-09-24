using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Application;

public enum ReviewMode
{
    File,
    LocalRepository,
    RemoteRepository,
    RagRepository,
    AgentRepository,
    GitHubPullRequest,
    Specialists
}

public enum ReviewStage
{
    Validating,
    Preparing,
    Reviewing,
    Finalizing,
    Completed
}

public sealed record ReviewRequest(
    ReviewMode Mode,
    string? Path = null,
    string? RemoteUrl = null,
    string? GitHubOwner = null,
    string? GitHubRepository = null,
    int PullRequestNumber = 0,
    int MaximumFiles = 50,
    long MaximumCharacters = 250_000);

public sealed record ReviewProgress(ReviewStage Stage, string Message, int? Percent = null);

public sealed record ReviewDraftComment(string Path, int Line, string Body);

public sealed record ReviewPublicationDraft(
    string Owner,
    string Repository,
    int PullRequestNumber,
    string HeadSha,
    IReadOnlyList<ReviewDraftComment> Comments);

public sealed record PublishReviewRequest(ReviewPublicationDraft Draft, bool HumanApproved);

public sealed record ReviewReport(
    ReviewMode Mode,
    string Subject,
    DateTimeOffset CompletedAt,
    IReadOnlyList<ReviewFinding> Findings,
    IReadOnlyList<AttributedFinding> AttributedFindings,
    RepositoryReviewStatistics? Statistics,
    IReadOnlyList<RepositoryFileFailure> Failures,
    IReadOnlyList<InvestigationStep> Investigation,
    ReviewPublicationDraft? PublicationDraft,
    ApiUsage? Usage,
    int? EmbeddingTokens,
    int SpecialistCalls,
    string Model);

public enum ApplicationErrorCode
{
    Validation,
    Configuration,
    File,
    Repository,
    Provider,
    Cancelled,
    Unexpected
}

public sealed record ApplicationError(ApplicationErrorCode Code, string Message);

public sealed record ApplicationResult<T>(T? Value, ApplicationError? Error)
{
    public bool IsSuccess => Error is null;
}

public static class ApplicationResult
{
    public static ApplicationResult<T> Success<T>(T value) => new(value, null);

    public static ApplicationResult<T> Failure<T>(ApplicationErrorCode code, string message) =>
        new(default, new ApplicationError(code, message));
}

public sealed record ConfigurationStatus(
    bool OpenAiApiKeyConfigured,
    bool OpenAiModelConfigured,
    bool GitHubTokenConfigured,
    string OpenAiModel,
    string EmbeddingModel,
    IReadOnlyList<string> Messages)
{
    public bool CanReview => OpenAiApiKeyConfigured && OpenAiModelConfigured;
}
