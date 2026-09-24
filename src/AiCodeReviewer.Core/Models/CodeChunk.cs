namespace AiCodeReviewer.Core.Models;

public sealed record CodeChunk(
    string File,
    int StartLine,
    int EndLine,
    string Content,
    LanguageProfile Language);
