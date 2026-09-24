using System.Text;
using AiCodeReviewer.Core.Abstractions;
using AiCodeReviewer.Core.Models;
using AiCodeReviewer.Core.Services;

namespace AiCodeReviewer.Tests;

public sealed class RagRepositoryReviewServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(Path.GetTempPath(), $"ai-reviewer-rag-{Guid.NewGuid():N}");

    public RagRepositoryReviewServiceTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
        File.WriteAllText(Path.Combine(_temporaryDirectory, "Alpha.cs"), "class Alpha { Beta value; }", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(_temporaryDirectory, "Beta.cs"), "class Beta { Alpha value; }", new UTF8Encoding(false));
    }

    [Fact]
    public async Task ReviewAsyncIndexesAndSuppliesCrossFileContext()
    {
        var reviewer = new CapturingReviewer();
        var service = new RagRepositoryReviewService(
            new RepositoryScanner(),
            new SourceFileLoader(),
            new SourceCodeChunker(),
            new DeterministicEmbeddingService(),
            new InMemoryVectorIndex(),
            reviewer);

        var result = await service.ReviewAsync(
            _temporaryDirectory,
            new RepositoryReviewOptions(10, 10_000));

        Assert.Equal(2, result.Statistics.FilesReviewed);
        Assert.Equal(2, result.Statistics.ChunksIndexed);
        Assert.Equal(2, result.Statistics.ContextChunksRetrieved);
        Assert.Equal(4, result.EmbeddingTokens);
        Assert.All(reviewer.Contexts, pair => Assert.DoesNotContain(pair.Value, chunk => chunk.File == pair.Key));
        Assert.All(reviewer.Contexts, pair => Assert.Single(pair.Value));
    }

    public void Dispose()
    {
        Directory.Delete(_temporaryDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private sealed class DeterministicEmbeddingService : IEmbeddingService
    {
        private static readonly float[] AlphaVector = [1f, 0f];
        private static readonly float[] OtherVector = [0f, 1f];

        public Task<EmbeddingBatch> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<float[]> vectors = inputs
                .Select(input => input.Contains("Alpha", StringComparison.Ordinal) ? AlphaVector : OtherVector)
                .ToArray();
            return Task.FromResult(new EmbeddingBatch(vectors, inputs.Count));
        }
    }

    private sealed class CapturingReviewer : IRetrievalAwareCodeReviewService
    {
        public Dictionary<string, IReadOnlyList<CodeChunk>> Contexts { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<CodeReviewResult> ReviewAsync(SourceFile sourceFile, CancellationToken cancellationToken = default) =>
            ReviewWithContextAsync(sourceFile, [], cancellationToken);

        public Task<CodeReviewResult> ReviewWithContextAsync(
            SourceFile sourceFile,
            IReadOnlyList<CodeChunk> relatedContext,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Contexts[sourceFile.Name] = relatedContext;
            return Task.FromResult(new CodeReviewResult([], new ApiUsage(1, 1, 2), "test-model"));
        }
    }
}
