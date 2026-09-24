using AiCodeReviewer.Core.Models;
using AiCodeReviewer.Core.Services;

namespace AiCodeReviewer.Tests;

public sealed class InMemoryVectorIndexTests
{
    [Fact]
    public void SearchRanksChunksByCosineSimilarityAndAppliesPredicate()
    {
        var profile = new LanguageDetector().Detect("Example.cs");
        var first = new CodeChunk("First.cs", 1, 2, "first", profile);
        var second = new CodeChunk("Second.cs", 1, 2, "second", profile);
        var index = new InMemoryVectorIndex();
        index.Replace(
        [
            new EmbeddedCodeChunk(first, [1, 0]),
            new EmbeddedCodeChunk(second, [0, 1])
        ]);

        var best = index.Search([0.9f, 0.1f], 1);
        var excludingFirst = index.Search([0.9f, 0.1f], 1, chunk => chunk.File != "First.cs");

        Assert.Equal("First.cs", Assert.Single(best).Chunk.File);
        Assert.Equal("Second.cs", Assert.Single(excludingFirst).Chunk.File);
    }
}
