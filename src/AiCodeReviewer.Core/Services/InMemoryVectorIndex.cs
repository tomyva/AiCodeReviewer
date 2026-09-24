using AiCodeReviewer.Core.Abstractions;
using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Core.Services;

public sealed class InMemoryVectorIndex : IVectorIndex
{
    private EmbeddedCodeChunk[] _chunks = [];

    public int Count => _chunks.Length;

    public void Replace(IReadOnlyList<EmbeddedCodeChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        if (chunks.Any(chunk => chunk.Vector.Length == 0))
        {
            throw new ArgumentException("Embedding vectors cannot be empty.", nameof(chunks));
        }

        _chunks = chunks.ToArray();
    }

    public IReadOnlyList<VectorSearchResult> Search(
        ReadOnlySpan<float> query,
        int count,
        Func<CodeChunk, bool>? predicate = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        if (query.IsEmpty)
        {
            throw new ArgumentException("The query vector cannot be empty.", nameof(query));
        }

        var results = new List<VectorSearchResult>();
        foreach (var embedded in _chunks)
        {
            if (predicate is not null && !predicate(embedded.Chunk))
            {
                continue;
            }

            if (embedded.Vector.Length != query.Length)
            {
                throw new InvalidOperationException("The query and index embedding dimensions do not match.");
            }

            results.Add(new VectorSearchResult(embedded.Chunk, CosineSimilarity(query, embedded.Vector)));
        }

        return results
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Chunk.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.Chunk.StartLine)
            .Take(count)
            .ToArray();
    }

    private static double CosineSimilarity(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        double dot = 0;
        double leftMagnitude = 0;
        double rightMagnitude = 0;
        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftMagnitude += left[index] * left[index];
            rightMagnitude += right[index] * right[index];
        }

        var denominator = Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude);
        return denominator == 0 ? 0 : dot / denominator;
    }
}
