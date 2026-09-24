namespace AiCodeReviewer.Core.Models;

public sealed record VectorSearchResult(CodeChunk Chunk, double Score);
