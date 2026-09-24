namespace AiCodeReviewer.Core.Models;

public sealed record EmbeddingBatch(IReadOnlyList<float[]> Vectors, int? InputTokens);
