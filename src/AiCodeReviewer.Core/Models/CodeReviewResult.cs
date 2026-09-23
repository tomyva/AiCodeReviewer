namespace AiCodeReviewer.Core.Models;

public sealed record CodeReviewResult(
    IReadOnlyList<ReviewFinding> Findings,
    ApiUsage? Usage,
    string Model);
