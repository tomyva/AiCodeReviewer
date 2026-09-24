using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Application;

public enum FindingSort
{
    SeverityDescending,
    SeverityAscending,
    File,
    Category
}

public sealed record FindingQuery(
    string? SearchText = null,
    ReviewSeverity? Severity = null,
    ReviewCategory? Category = null,
    FindingSort Sort = FindingSort.SeverityDescending);

public static class ReviewFindingQuery
{
    public static IReadOnlyList<ReviewFinding> Apply(IEnumerable<ReviewFinding> findings, FindingQuery query)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(query);
        var filtered = findings;
        if (query.Severity is not null)
        {
            filtered = filtered.Where(finding => finding.Severity == query.Severity);
        }

        if (query.Category is not null)
        {
            filtered = filtered.Where(finding => finding.Category == query.Category);
        }

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var search = query.SearchText.Trim();
            filtered = filtered.Where(finding =>
                finding.File.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                finding.Explanation.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                finding.SuggestedImprovement.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (finding.Location?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return query.Sort switch
        {
            FindingSort.SeverityAscending => filtered.OrderBy(finding => finding.Severity).ThenBy(finding => finding.File, StringComparer.OrdinalIgnoreCase).ToArray(),
            FindingSort.File => filtered.OrderBy(finding => finding.File, StringComparer.OrdinalIgnoreCase).ThenByDescending(finding => finding.Severity).ToArray(),
            FindingSort.Category => filtered.OrderBy(finding => finding.Category).ThenByDescending(finding => finding.Severity).ToArray(),
            _ => filtered.OrderByDescending(finding => finding.Severity).ThenBy(finding => finding.File, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }
}
