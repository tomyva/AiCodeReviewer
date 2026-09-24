using AiCodeReviewer.Application;
using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Tests;

public sealed class ReviewIntegrationTests
{
    [Fact]
    public void FindingQueryFiltersSearchesAndSortsDeterministically()
    {
        var findings = new[]
        {
            Finding(ReviewSeverity.Low, ReviewCategory.Maintainability, "Beta.cs", "Long method"),
            Finding(ReviewSeverity.Critical, ReviewCategory.Security, "Auth.cs", "Unsafe token validation"),
            Finding(ReviewSeverity.High, ReviewCategory.Security, "Api.cs", "Unsafe input")
        };

        var result = ReviewFindingQuery.Apply(
            findings,
            new FindingQuery("unsafe", Category: ReviewCategory.Security, Sort: FindingSort.File));

        Assert.Equal(2, result.Count);
        Assert.Equal("Api.cs", result[0].File);
        Assert.Equal("Auth.cs", result[1].File);
    }

    [Fact]
    public void UserStatePersistsOnlySafePreferencesAndBoundedMetadata()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ai-review-state-{Guid.NewGuid():N}");
        try
        {
            var store = new JsonUserStateStore(directory);
            var preferences = new UserPreferences(ReviewMode.RemoteRepository, 25, 100_000, ExportFormat.Json, AppTheme.Dark);
            store.SavePreferences(preferences);
            for (var index = 0; index < 25; index++)
            {
                store.AddRecentReview(new RecentReviewEntry(Guid.NewGuid(), DateTimeOffset.UnixEpoch.AddMinutes(index), ReviewMode.File, index, null));
            }

            Assert.Equal(preferences, store.LoadPreferences());
            Assert.Equal(20, store.LoadRecentReviews().Count);
            var persisted = string.Join(Environment.NewLine, Directory.GetFiles(directory).Select(File.ReadAllText));
            Assert.DoesNotContain("OPENAI_API_KEY", persisted, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("GITHUB_TOKEN", persisted, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("source", persisted, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task ApplicationRecordsSafeRecentReviewMetadata()
    {
        var state = new MemoryStateStore();
        var report = new ReviewReport(ReviewMode.File, "secret-source.cs", DateTimeOffset.UnixEpoch, [], [], null, [], [], null, null, null, 0, "model");
        var service = new ReviewApplicationService(new StaticDispatcher(report), state);

        var result = await service.ReviewAsync(new ReviewRequest(ReviewMode.File, Path: "secret-source.cs"));

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(state.Entries);
        Assert.Equal(ReviewMode.File, entry.Mode);
        Assert.Null(entry.ReportPath);
    }

    private static ReviewFinding Finding(ReviewSeverity severity, ReviewCategory category, string file, string explanation) =>
        new(severity, category, file, explanation, "line 1", "Improve it");

    private sealed class MemoryStateStore : IUserStateStore
    {
        public List<RecentReviewEntry> Entries { get; } = [];

        public UserPreferences LoadPreferences() => new();

        public void SavePreferences(UserPreferences preferences) { }

        public IReadOnlyList<RecentReviewEntry> LoadRecentReviews() => Entries;

        public void AddRecentReview(RecentReviewEntry entry) => Entries.Add(entry);
    }

    private sealed class StaticDispatcher(ReviewReport report) : IReviewWorkflowDispatcher
    {
        public Task<ReviewReport> ReviewAsync(ReviewRequest request, IProgress<ReviewProgress>? progress, CancellationToken cancellationToken) => Task.FromResult(report);

        public Task PublishAsync(PublishReviewRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
