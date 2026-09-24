using AiCodeReviewer.Core.Abstractions;
using AiCodeReviewer.Core.Models;
using AiCodeReviewer.Core.Services;
using AiCodeReviewer.GitHub;

namespace AiCodeReviewer.Tests;

public sealed class PullRequestReviewServiceTests
{
    private const string Diff = """
        diff --git a/Example.cs b/Example.cs
        --- a/Example.cs
        +++ b/Example.cs
        @@ -1,1 +1,2 @@
         class Example {}
        +// TODO fix
        """;

    [Fact]
    public async Task ReviewAsyncMapsFindingToAddedGitHubLine()
    {
        var service = new PullRequestReviewService(
            new StubGitHubClient(),
            new StubReviewer(),
            new LanguageDetector());

        var (_, result) = await service.ReviewAsync(new PullRequestReference("owner", "repo", 1));

        var comment = Assert.Single(result.DraftComments);
        Assert.Equal("Example.cs", comment.Path);
        Assert.Equal(2, comment.Line);
        Assert.Contains("Suggested improvement", comment.Body);
    }

    [Fact]
    public async Task GitHubClientRefusesPublishingWithoutHumanApproval()
    {
        using var httpClient = new HttpClient();
        var client = new GitHubPullRequestClient(httpClient, new GitHubOptions("token", new Uri("https://api.github.com/")));
        var pullRequest = new PullRequestData(new PullRequestReference("owner", "repo", 1), "sha", Diff);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.PostReviewAsync(pullRequest, [], humanApproved: false));
    }

    private sealed class StubGitHubClient : IGitHubPullRequestClient
    {
        public Task<PullRequestData> GetAsync(PullRequestReference reference, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PullRequestData(reference, "sha", Diff));

        public Task PostReviewAsync(
            PullRequestData pullRequest,
            IReadOnlyList<DraftReviewComment> comments,
            bool humanApproved,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubReviewer : ICodeReviewService
    {
        public Task<CodeReviewResult> ReviewAsync(SourceFile sourceFile, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ReviewFinding> findings =
            [
                new ReviewFinding(
                    ReviewSeverity.Medium,
                    ReviewCategory.Bug,
                    sourceFile.Name,
                    "The new line is incomplete.",
                    "line 2",
                    "Complete the implementation.")
            ];
            return Task.FromResult(new CodeReviewResult(findings, null, "test"));
        }
    }
}
