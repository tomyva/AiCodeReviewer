namespace AiCodeReviewer.Core.Models;

public sealed record SpecialistProfile(SpecialistKind Kind, string Guidance)
{
    public static IReadOnlyList<SpecialistProfile> All { get; } =
    [
        new(SpecialistKind.Correctness, "Focus only on bugs, logic errors, state, concurrency, edge cases, and missing error handling."),
        new(SpecialistKind.Security, "Focus only on trust boundaries, injection, authentication, authorization, secrets, unsafe data handling, and exploitable behavior."),
        new(SpecialistKind.Performance, "Focus only on algorithmic cost, allocations, I/O, blocking, concurrency, resource usage, and measurable scalability risks."),
        new(SpecialistKind.Architecture, "Focus only on design, coupling, cohesion, maintainability, API contracts, ownership, and high-value refactoring."),
        new(SpecialistKind.Testing, "Focus only on missing tests, weak assertions, important branches, failure paths, concurrency, and regression coverage.")
    ];
}
