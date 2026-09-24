namespace AiCodeReviewer.Core.Models;

public sealed record RepositoryReviewStatistics(
    int FilesDiscovered,
    int FilesReviewed,
    int FilesSkipped,
    int FilesFailed,
    long CharactersReviewed,
    int ChunksIndexed,
    int ContextChunksRetrieved);
