using AiCodeReviewer.Core.Abstractions;
using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Core.Services;

public sealed class MultiAgentReviewCoordinator
{
    private readonly ISpecialistCodeReviewService _reviewer;

    public MultiAgentReviewCoordinator(ISpecialistCodeReviewService reviewer) =>
        _reviewer = reviewer ?? throw new ArgumentNullException(nameof(reviewer));

    public async Task<MultiAgentReviewResult> ReviewAsync(
        SourceFile sourceFile,
        IReadOnlyList<SpecialistProfile>? specialists = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceFile);
        specialists ??= SpecialistProfile.All;
        if (specialists.Count == 0 || specialists.Count > SpecialistProfile.All.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(specialists), "Choose between one and five specialists.");
        }

        var candidates = new List<(ReviewFinding Finding, SpecialistKind Specialist)>();
        var usages = new List<ApiUsage>();
        string? model = null;
        foreach (var specialist in specialists)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _reviewer.ReviewAsSpecialistAsync(sourceFile, specialist, cancellationToken);
            candidates.AddRange(result.Findings.Select(finding => (finding, specialist.Kind)));
            if (result.Usage is not null)
            {
                usages.Add(result.Usage);
            }

            model ??= result.Model;
        }

        var findings = candidates
            .GroupBy(candidate => CreateDeduplicationKey(candidate.Finding), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var best = group
                    .OrderByDescending(item => item.Finding.Severity)
                    .ThenByDescending(item => item.Finding.Explanation.Length)
                    .First()
                    .Finding;
                IReadOnlyList<SpecialistKind> sources = group.Select(item => item.Specialist).Distinct().Order().ToArray();
                return new AttributedFinding(best, sources);
            })
            .OrderByDescending(item => item.Finding.Severity)
            .ThenBy(item => item.Finding.File, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MultiAgentReviewResult(findings, specialists.Count, Aggregate(usages), model ?? "unavailable");
    }

    private static string CreateDeduplicationKey(ReviewFinding finding)
    {
        var location = string.IsNullOrWhiteSpace(finding.Location) ? "no-location" : finding.Location.Trim();
        var explanationPrefix = finding.Explanation.Length <= 80 ? finding.Explanation : finding.Explanation[..80];
        return $"{finding.File}|{finding.Category}|{location}|{explanationPrefix}";
    }

    private static ApiUsage? Aggregate(List<ApiUsage> usages) => usages.Count == 0
        ? null
        : new ApiUsage(
            Sum(usages.Select(usage => usage.InputTokens)),
            Sum(usages.Select(usage => usage.OutputTokens)),
            Sum(usages.Select(usage => usage.TotalTokens)));

    private static int? Sum(IEnumerable<int?> values)
    {
        var materialized = values.ToArray();
        return materialized.All(value => value.HasValue) ? materialized.Sum(value => value!.Value) : null;
    }
}
