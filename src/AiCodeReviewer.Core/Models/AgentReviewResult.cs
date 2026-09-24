namespace AiCodeReviewer.Core.Models;

public sealed record AgentReviewResult(
    IReadOnlyList<ReviewFinding> Findings,
    IReadOnlyList<InvestigationStep> Investigation,
    ApiUsage? Usage,
    string Model);
