namespace AiCodeReviewer.Core.Models;

public sealed record ApiUsage(int? InputTokens, int? OutputTokens, int? TotalTokens);
