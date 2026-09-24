using AiCodeReviewer.Core.Models;
using AiCodeReviewer.Core.Services;
using AiCodeReviewer.GitHub;
using AiCodeReviewer.OpenAI;

namespace AiCodeReviewer.Application;

public sealed class DefaultReviewWorkflowDispatcher : IReviewWorkflowDispatcher
{
    private readonly HttpClient _httpClient;

    public DefaultReviewWorkflowDispatcher(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<ReviewReport> ReviewAsync(
        ReviewRequest request,
        IProgress<ReviewProgress>? progress,
        CancellationToken cancellationToken)
    {
        var providerOptions = OpenAiOptions.FromEnvironment();
        var reviewer = new OpenAiCodeReviewService(_httpClient, providerOptions);
        progress?.Report(new ReviewProgress(ReviewStage.Preparing, "Preparing review inputs", 10));

        return request.Mode switch
        {
            ReviewMode.File => await ReviewFileAsync(request, reviewer, cancellationToken),
            ReviewMode.LocalRepository => await ReviewRepositoryAsync(request, reviewer, false, progress, cancellationToken),
            ReviewMode.RemoteRepository => await ReviewRemoteAsync(request, reviewer, progress, cancellationToken),
            ReviewMode.RagRepository => await ReviewRepositoryAsync(request, reviewer, true, progress, cancellationToken),
            ReviewMode.AgentRepository => await ReviewAgentAsync(request, providerOptions, cancellationToken),
            ReviewMode.GitHubPullRequest => await ReviewPullRequestAsync(request, reviewer, cancellationToken),
            ReviewMode.Specialists => await ReviewSpecialistsAsync(request, reviewer, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(request), "Unsupported review mode.")
        };
    }

    public async Task PublishAsync(PublishReviewRequest request, CancellationToken cancellationToken)
    {
        var draft = request.Draft;
        var reference = new PullRequestReference(draft.Owner, draft.Repository, draft.PullRequestNumber);
        var pullRequest = new PullRequestData(reference, draft.HeadSha, string.Empty);
        var comments = draft.Comments
            .Select(comment => new DraftReviewComment(comment.Path, comment.Line, comment.Body))
            .ToArray();
        var client = new GitHubPullRequestClient(_httpClient, GitHubOptions.FromEnvironment());
        await client.PostReviewAsync(pullRequest, comments, request.HumanApproved, cancellationToken);
    }

    private static async Task<ReviewReport> ReviewFileAsync(
        ReviewRequest request,
        OpenAiCodeReviewService reviewer,
        CancellationToken cancellationToken)
    {
        var source = await new SourceFileLoader().LoadAsync(request.Path!, cancellationToken);
        var result = await reviewer.ReviewAsync(source, cancellationToken);
        return CreateReport(request.Mode, source.Path, result.Findings, usage: result.Usage, model: result.Model);
    }

    private async Task<ReviewReport> ReviewRepositoryAsync(
        ReviewRequest request,
        OpenAiCodeReviewService reviewer,
        bool useRag,
        IProgress<ReviewProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new ReviewProgress(ReviewStage.Reviewing, useRag ? "Indexing and reviewing repository" : "Reviewing repository", 25));
        var options = new RepositoryReviewOptions(request.MaximumFiles, request.MaximumCharacters);
        RepositoryReviewResult result;

        if (useRag)
        {
            var providerOptions = OpenAiOptions.FromEnvironment();
            var service = new RagRepositoryReviewService(
                new RepositoryScanner(),
                new SourceFileLoader(),
                new SourceCodeChunker(),
                new OpenAiEmbeddingService(_httpClient, OpenAiEmbeddingOptions.FromEnvironment(providerOptions)),
                new InMemoryVectorIndex(),
                reviewer);
            result = await service.ReviewAsync(request.Path!, options, cancellationToken);
        }
        else
        {
            var service = new RepositoryReviewService(new RepositoryScanner(), new SourceFileLoader(), reviewer);
            result = await service.ReviewAsync(request.Path!, options, cancellationToken);
        }

        progress?.Report(new ReviewProgress(ReviewStage.Finalizing, "Preparing repository report", 90));
        return CreateReport(
            request.Mode,
            Path.GetFullPath(request.Path!),
            result.Findings,
            statistics: result.Statistics,
            failures: result.Failures,
            usage: result.Usage,
            embeddingTokens: result.EmbeddingTokens,
            model: result.Model);
    }

    private async Task<ReviewReport> ReviewRemoteAsync(
        ReviewRequest request,
        OpenAiCodeReviewService reviewer,
        IProgress<ReviewProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new ReviewProgress(ReviewStage.Preparing, "Creating an isolated shallow checkout", 15));
        using var checkout = await RemoteGitRepository.CloneAsync(request.RemoteUrl!, cancellationToken);
        var localRequest = request with { Mode = ReviewMode.RemoteRepository, Path = checkout.Path };
        return (await ReviewRepositoryAsync(localRequest, reviewer, false, progress, cancellationToken)) with
        {
            Subject = request.RemoteUrl!
        };
    }

    private async Task<ReviewReport> ReviewAgentAsync(
        ReviewRequest request,
        OpenAiOptions options,
        CancellationToken cancellationToken)
    {
        var result = await new OpenAiAgentReviewService(_httpClient, options)
            .ReviewRepositoryAsync(request.Path!, new AgentReviewOptions(), cancellationToken);
        return CreateReport(
            request.Mode,
            Path.GetFullPath(request.Path!),
            result.Findings,
            investigation: result.Investigation,
            usage: result.Usage,
            model: result.Model);
    }

    private async Task<ReviewReport> ReviewPullRequestAsync(
        ReviewRequest request,
        OpenAiCodeReviewService reviewer,
        CancellationToken cancellationToken)
    {
        var github = new GitHubPullRequestClient(_httpClient, GitHubOptions.FromEnvironment());
        var service = new PullRequestReviewService(github, reviewer, new LanguageDetector());
        var reference = new PullRequestReference(request.GitHubOwner!, request.GitHubRepository!, request.PullRequestNumber);
        var (pullRequest, result) = await service.ReviewAsync(reference, cancellationToken);
        var draft = new ReviewPublicationDraft(
            reference.Owner,
            reference.Repository,
            reference.Number,
            pullRequest.HeadSha,
            result.DraftComments.Select(comment => new ReviewDraftComment(comment.Path, comment.Line, comment.Body)).ToArray());
        return CreateReport(
            request.Mode,
            $"{reference.Owner}/{reference.Repository}#{reference.Number}",
            result.Findings,
            publicationDraft: draft,
            usage: result.Usage,
            model: result.Model);
    }

    private static async Task<ReviewReport> ReviewSpecialistsAsync(
        ReviewRequest request,
        OpenAiCodeReviewService reviewer,
        CancellationToken cancellationToken)
    {
        var source = await new SourceFileLoader().LoadAsync(request.Path!, cancellationToken);
        var result = await new MultiAgentReviewCoordinator(reviewer).ReviewAsync(source, cancellationToken: cancellationToken);
        return CreateReport(
            request.Mode,
            source.Path,
            result.Findings.Select(item => item.Finding).ToArray(),
            attributedFindings: result.Findings,
            specialistCalls: result.SpecialistCalls,
            usage: result.Usage,
            model: result.Model);
    }

    private static ReviewReport CreateReport(
        ReviewMode mode,
        string subject,
        IReadOnlyList<ReviewFinding> findings,
        IReadOnlyList<AttributedFinding>? attributedFindings = null,
        RepositoryReviewStatistics? statistics = null,
        IReadOnlyList<RepositoryFileFailure>? failures = null,
        IReadOnlyList<InvestigationStep>? investigation = null,
        ReviewPublicationDraft? publicationDraft = null,
        ApiUsage? usage = null,
        int? embeddingTokens = null,
        int specialistCalls = 0,
        string model = "unavailable") =>
        new(
            mode,
            subject,
            DateTimeOffset.UtcNow,
            findings,
            attributedFindings ?? [],
            statistics,
            failures ?? [],
            investigation ?? [],
            publicationDraft,
            usage,
            embeddingTokens,
            specialistCalls,
            model);
}
