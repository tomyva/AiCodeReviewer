namespace AiCodeReviewer.Core.Models;

public sealed record AttributedFinding(ReviewFinding Finding, IReadOnlyList<SpecialistKind> Specialists);
