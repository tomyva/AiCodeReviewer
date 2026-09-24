using AiCodeReviewer.Application;
using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Tests;

public sealed class ReviewApplicationServiceTests
{
    [Fact]
    public async Task ReviewAsyncValidRequestReturnsDispatcherReportAndProgress()
    {
        var dispatcher = new FakeDispatcher(CreateReport());
        var service = new ReviewApplicationService(dispatcher);
        var updates = new List<ReviewProgress>();

        var result = await service.ReviewAsync(
            new ReviewRequest(ReviewMode.File, Path: "sample.cs"),
            new InlineProgress<ReviewProgress>(updates.Add));

        Assert.True(result.IsSuccess);
        Assert.Same(dispatcher.Report, result.Value);
        Assert.Equal(2, updates.Count);
        Assert.Equal(ReviewStage.Validating, updates[0].Stage);
        Assert.Equal(ReviewStage.Completed, updates[1].Stage);
    }

    [Theory]
    [InlineData(ReviewMode.File)]
    [InlineData(ReviewMode.LocalRepository)]
    [InlineData(ReviewMode.RemoteRepository)]
    public async Task ReviewAsyncMissingRequiredInputReturnsValidationError(ReviewMode mode)
    {
        var service = new ReviewApplicationService(new FakeDispatcher(CreateReport()));

        var result = await service.ReviewAsync(new ReviewRequest(mode));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationErrorCode.Validation, result.Error?.Code);
    }

    [Fact]
    public async Task ReviewAsyncCancelledDispatcherReturnsCancelledError()
    {
        var service = new ReviewApplicationService(new FakeDispatcher(CreateReport()) { CancelReview = true });

        var result = await service.ReviewAsync(new ReviewRequest(ReviewMode.File, Path: "sample.cs"));

        Assert.Equal(ApplicationErrorCode.Cancelled, result.Error?.Code);
    }

    [Fact]
    public async Task PublishAsyncWithoutFreshConfirmationIsRejected()
    {
        var service = new ReviewApplicationService(new FakeDispatcher(CreateReport()));
        var draft = new ReviewPublicationDraft("owner", "repo", 1, "sha", []);

        var result = await service.PublishPullRequestReviewAsync(new PublishReviewRequest(draft, false));

        Assert.Equal(ApplicationErrorCode.Validation, result.Error?.Code);
    }

    [Fact]
    public void ExportersProduceStructuredJsonAndMarkdown()
    {
        var report = CreateReport();

        var json = ReviewReportExporter.ToJson(report);
        var markdown = ReviewReportExporter.ToMarkdown(report);

        Assert.Contains("\"severity\": \"High\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("## Findings", markdown, StringComparison.Ordinal);
        Assert.Contains("Suggested improvement", markdown, StringComparison.Ordinal);
    }

    private static ReviewReport CreateReport()
    {
        var finding = new ReviewFinding(
            ReviewSeverity.High,
            ReviewCategory.Security,
            "sample.cs",
            "Unsafe input handling.",
            "line 7",
            "Validate the input.");
        return new ReviewReport(
            ReviewMode.File,
            "sample.cs",
            DateTimeOffset.UnixEpoch,
            [finding],
            [],
            null,
            [],
            [],
            null,
            null,
            null,
            0,
            "test-model");
    }

    private sealed class FakeDispatcher(ReviewReport report) : IReviewWorkflowDispatcher
    {
        public ReviewReport Report { get; } = report;

        public bool CancelReview { get; init; }

        public Task<ReviewReport> ReviewAsync(
            ReviewRequest request,
            IProgress<ReviewProgress>? progress,
            CancellationToken cancellationToken) =>
            CancelReview ? Task.FromCanceled<ReviewReport>(new CancellationToken(true)) : Task.FromResult(Report);

        public Task PublishAsync(PublishReviewRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }
}
