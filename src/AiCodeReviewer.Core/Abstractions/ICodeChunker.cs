using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Core.Abstractions;

public interface ICodeChunker
{
    IReadOnlyList<CodeChunk> Chunk(SourceFile sourceFile);
}
