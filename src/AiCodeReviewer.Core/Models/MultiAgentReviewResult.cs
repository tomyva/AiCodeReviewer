namespace AiCodeReviewer.Core.Models;

public sealed record MultiAgentReviewResult(
    IReadOnlyList<AttributedFinding> Findings,
    int SpecialistCalls,
    ApiUsage? Usage,
    string Model);
