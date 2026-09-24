namespace AiCodeReviewer.Core.Models;

public sealed record RepositoryScanResult(
    string RootPath,
    IReadOnlyList<RepositoryFile> Files,
    int FilesDiscovered,
    int FilesSkipped);
