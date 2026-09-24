using AiCodeReviewer.Core.Models;
using AiCodeReviewer.Core.Services;

namespace AiCodeReviewer.Tests;

public sealed class SourceCodeChunkerTests
{
    [Fact]
    public void ChunkSplitsLargeFileAndPreservesLineLocations()
    {
        var profile = new LanguageDetector().Detect("Example.cs");
        var content = string.Join('\n', Enumerable.Range(1, 20).Select(number => $"// line {number:D2} with content"));
        var source = new SourceFile("Example.cs", "Example.cs", content, profile);

        var chunks = new SourceCodeChunker(maximumCharacters: 120, overlapLines: 2).Chunk(source);

        Assert.True(chunks.Count > 1);
        Assert.Equal(1, chunks[0].StartLine);
        Assert.Equal(20, chunks[^1].EndLine);
        Assert.True(chunks[1].StartLine <= chunks[0].EndLine);
        Assert.All(chunks, chunk => Assert.Equal("Example.cs", chunk.File));
    }
}
