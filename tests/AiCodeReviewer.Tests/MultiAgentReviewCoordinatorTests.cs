using AiCodeReviewer.Core.Abstractions;
using AiCodeReviewer.Core.Models;
using AiCodeReviewer.Core.Services;

namespace AiCodeReviewer.Tests;

public sealed class MultiAgentReviewCoordinatorTests
{
    [Fact]
    public async Task ReviewAsyncRunsSpecialistsDeduplicatesAndPreservesAttribution()
    {
        var language = new LanguageDetector().Detect("Example.cs");
        var source = new SourceFile("Example.cs", "Example.cs", "class Example {}", language);
        var specialists = SpecialistProfile.All.Take(2).ToArray();
        var coordinator = new MultiAgentReviewCoordinator(new DuplicateFindingReviewer());

        var result = await coordinator.ReviewAsync(source, specialists);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(2, finding.Specialists.Count);
        Assert.Equal(2, result.SpecialistCalls);
        Assert.Equal(new ApiUsage(20, 10, 30), result.Usage);
    }

    private sealed class DuplicateFindingReviewer : ISpecialistCodeReviewService
    {
        public Task<CodeReviewResult> ReviewAsync(SourceFile sourceFile, CancellationToken cancellationToken = default) =>
            ReviewAsSpecialistAsync(sourceFile, SpecialistProfile.All[0], cancellationToken);

        public Task<CodeReviewResult> ReviewAsSpecialistAsync(
            SourceFile sourceFile,
            SpecialistProfile specialist,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ReviewFinding> findings =
            [
                new ReviewFinding(
                    ReviewSeverity.High,
                    ReviewCategory.Bug,
                    sourceFile.Name,
                    "A shared mutable value can race.",
                    "line 1",
                    "Synchronize access to the value.")
            ];
            return Task.FromResult(new CodeReviewResult(findings, new ApiUsage(10, 5, 15), "test-model"));
        }
    }
}
