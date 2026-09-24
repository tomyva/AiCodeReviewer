namespace AiCodeReviewer.Core.Models;

public sealed record RepositoryFile(
    string FullPath,
    string RelativePath,
    long SizeBytes,
    LanguageProfile Language);
