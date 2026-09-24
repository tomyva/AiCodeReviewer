namespace AiCodeReviewer.Core.Models;

public sealed record LanguageProfile(
    ProgrammingLanguage Language,
    string DisplayName,
    string CodeFence,
    string ReviewGuidance);
