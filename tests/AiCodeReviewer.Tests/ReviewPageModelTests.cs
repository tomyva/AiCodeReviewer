using AiCodeReviewer.Application;
using AiCodeReviewer.Blazor.Services;

namespace AiCodeReviewer.Tests;

public sealed class ReviewPageModelTests
{
    [Fact]
    public void ModeControlsExposeOnlyRelevantInputs()
    {
        using var model = new ReviewPageModel(new FakeApplication());

        model.Mode = ReviewMode.RemoteRepository;
        Assert.True(model.UsesRemoteUrl);
        Assert.True(model.UsesRepositoryLimits);
        Assert.False(model.UsesPath);

        model.Mode = ReviewMode.GitHubPullRequest;
        Assert.True(model.UsesPullRequest);
        Assert.False(model.UsesRepositoryLimits);
    }

    [Fact]
    public async Task StartAsyncStoresSuccessfulReport()
    {
        var application = new FakeApplication();
        using var model = new ReviewPageModel(application) { Path = "sample.cs" };

        await model.StartAsync();

        Assert.False(model.IsBusy);
        Assert.Equal("Review completed", model.Status);
        Assert.Equal("sample.cs", model.Report?.Subject);
        Assert.Contains("data:application/json", model.JsonDownloadUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishAsyncRequiresPageConfirmation()
    {
        var application = new FakeApplication();
        using var model = new ReviewPageModel(application) { Path = "sample.cs" };
        await model.StartAsync();

        await model.PublishAsync();

        Assert.Equal(ApplicationErrorCode.Validation, model.Error?.Code);
        Assert.False(application.PublishCalled);
    }

    private sealed class FakeApplication : ICodeReviewerApplication
    {
        private readonly ReviewPublicationDraft _draft = new("owner", "repo", 1, "sha", []);

        public ReviewReport Report => new(
            ReviewMode.File,
            "sample.cs",
            DateTimeOffset.UnixEpoch,
            [],
            [],
            null,
            [],
            [],
            _draft,
            null,
            null,
            0,
            "test-model");

        public bool PublishCalled { get; private set; }

        public ConfigurationStatus GetConfigurationStatus() => new(true, true, false, "test-model", "embedding-model", []);

        public Task<ApplicationResult<ReviewReport>> ReviewAsync(
            ReviewRequest request,
            IProgress<ReviewProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ApplicationResult.Success(Report));

        public Task<ApplicationResult<bool>> PublishPullRequestReviewAsync(
            PublishReviewRequest request,
            CancellationToken cancellationToken = default)
        {
            PublishCalled = true;
            return Task.FromResult(ApplicationResult.Success(true));
        }

        public string ExportJson(ReviewReport report) => "{}";

        public string ExportMarkdown(ReviewReport report) => "# report";
    }
}
