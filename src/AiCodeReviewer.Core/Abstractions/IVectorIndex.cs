using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Core.Abstractions;

public interface IVectorIndex
{
    int Count { get; }

    void Replace(IReadOnlyList<EmbeddedCodeChunk> chunks);

    IReadOnlyList<VectorSearchResult> Search(
        ReadOnlySpan<float> query,
        int count,
        Func<CodeChunk, bool>? predicate = null);
}
