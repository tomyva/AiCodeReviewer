using AiCodeReviewer.Application;
using AiCodeReviewer.Wpf;

namespace AiCodeReviewer.Wpf.Tests;

public sealed class DesktopReviewViewModelTests
{
    [Fact]
    public void BrowseUsesFilePickerForFileModeAndFolderPickerForRepositoryMode()
    {
        var dialogs = new FakeDialogs();
        using var model = new DesktopReviewViewModel(new FakeApplication(), dialogs);

        model.Mode = ReviewMode.File;
        model.Browse();
        Assert.Equal("chosen.cs", model.Path);

        model.Mode = ReviewMode.LocalRepository;
        model.Browse();
        Assert.Equal("chosen-folder", model.Path);
    }

    [Fact]
    public async Task StartAsyncStoresReportAndEnablesExports()
    {
        using var model = new DesktopReviewViewModel(new FakeApplication(), new FakeDialogs()) { Path = "chosen.cs" };

        await model.StartAsync();

        Assert.True(model.HasReport);
        Assert.Equal("Review completed", model.Status);
        Assert.True(model.ExportJsonCommand.CanExecute(null));
    }

    [Fact]
    public async Task PublishNeedsFreshConfirmation()
    {
        var application = new FakeApplication();
        using var model = new DesktopReviewViewModel(application, new FakeDialogs()) { Path = "chosen.cs" };
        await model.StartAsync();

        await model.PublishAsync();

        Assert.False(application.PublishCalled);
        Assert.Equal(ApplicationErrorCode.Validation, model.Error?.Code);
    }

    private sealed class FakeDialogs : IDesktopDialogService
    {
        public string? PickSourceFile() => "chosen.cs";

        public string? PickFolder() => "chosen-folder";

        public string? PickExportPath(string extension, string filter) => null;
    }

    private sealed class FakeApplication : ICodeReviewerApplication
    {
        public bool PublishCalled { get; private set; }

        public ConfigurationStatus GetConfigurationStatus() => new(true, true, false, "test-model", "embedding-model", []);

        public Task<ApplicationResult<ReviewReport>> ReviewAsync(
            ReviewRequest request,
            IProgress<ReviewProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var draft = new ReviewPublicationDraft("owner", "repo", 1, "sha", []);
            var report = new ReviewReport(
                request.Mode,
                request.Path ?? "subject",
                DateTimeOffset.UnixEpoch,
                [],
                [],
                null,
                [],
                [],
                draft,
                null,
                null,
                0,
                "test-model");
            return Task.FromResult(ApplicationResult.Success(report));
        }

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
