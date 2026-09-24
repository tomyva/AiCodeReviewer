namespace AiCodeReviewer.Core.Models;

public sealed record InvestigationStep(int Number, string Tool, string Arguments, int OutputCharacters);
