using System.Text;
using AiCodeReviewer.Core.Abstractions;
using AiCodeReviewer.Core.Models;
using AiCodeReviewer.Core.Services;

namespace AiCodeReviewer.Tests;

public sealed class RepositoryReviewServiceTests : IDisposable
{
    private static readonly string[] ExpectedReviewedFiles = ["a.cs", "b.py"];
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"ai-reviewer-service-{Guid.NewGuid():N}");

    public RepositoryReviewServiceTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        CreateFile("a.cs", "class A {}");
        CreateFile("b.py", "class B: pass");
        CreateFile("c.java", "class C {}");
    }

    [Fact]
    public async Task ReviewAsyncHonorsFileBudgetAndAggregatesResults()
    {
        var service = new RepositoryReviewService(
            new RepositoryScanner(),
            new SourceFileLoader(),
            new StubCodeReviewService());

        var result = await service.ReviewAsync(
            _temporaryDirectory,
            new RepositoryReviewOptions(MaximumFiles: 2, MaximumTotalCharacters: 1_000));

        Assert.Equal(3, result.Statistics.FilesDiscovered);
        Assert.Equal(2, result.Statistics.FilesReviewed);
        Assert.Equal(1, result.Statistics.FilesSkipped);
        Assert.Equal(0, result.Statistics.FilesFailed);
        Assert.Equal(2, result.Findings.Count);
        Assert.All(result.Findings, finding => Assert.Contains(finding.File, ExpectedReviewedFiles));
        Assert.Equal(new ApiUsage(20, 10, 30), result.Usage);
    }

    [Fact]
    public async Task ReviewAsyncHonorsCharacterBudgetWithoutConcatenatingFiles()
    {
        var reviewer = new StubCodeReviewService();
        var service = new RepositoryReviewService(new RepositoryScanner(), new SourceFileLoader(), reviewer);

        var result = await service.ReviewAsync(
            _temporaryDirectory,
            new RepositoryReviewOptions(MaximumFiles: 10, MaximumTotalCharacters: 12));

        Assert.Single(reviewer.ReviewedFiles);
        Assert.Equal(1, result.Statistics.FilesReviewed);
        Assert.Equal(2, result.Statistics.FilesSkipped);
    }

    private void CreateFile(string name, string content) =>
        File.WriteAllText(Path.Combine(_temporaryDirectory, name), content, new UTF8Encoding(false));

    public void Dispose()
    {
        Directory.Delete(_temporaryDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private sealed class StubCodeReviewService : ICodeReviewService
    {
        public List<string> ReviewedFiles { get; } = [];

        public Task<CodeReviewResult> ReviewAsync(SourceFile sourceFile, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReviewedFiles.Add(sourceFile.Name);
            IReadOnlyList<ReviewFinding> findings =
            [
                new ReviewFinding(
                    ReviewSeverity.Low,
                    ReviewCategory.Testing,
                    sourceFile.Name,
                    "Add a focused test.",
                    "line 1",
                    "Cover the primary behavior with an automated test.")
            ];

            return Task.FromResult(new CodeReviewResult(findings, new ApiUsage(10, 5, 15), "test-model"));
        }
    }
}
