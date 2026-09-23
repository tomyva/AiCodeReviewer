namespace AiCodeReviewer.Core.Models;

public sealed record ReviewFinding(
    ReviewSeverity Severity,
    ReviewCategory Category,
    string Explanation,
    string? Location,
    string SuggestedImprovement);
