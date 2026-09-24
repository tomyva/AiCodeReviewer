using System.Text.RegularExpressions;
using AiCodeReviewer.Core.Abstractions;
using AiCodeReviewer.Core.Models;
using AiCodeReviewer.Core.Services;

namespace AiCodeReviewer.GitHub;

public sealed partial class PullRequestReviewService
{
    private readonly IGitHubPullRequestClient _github;
    private readonly ICodeReviewService _reviewer;
    private readonly ILanguageDetector _languageDetector;

    public PullRequestReviewService(
        IGitHubPullRequestClient github,
        ICodeReviewService reviewer,
        ILanguageDetector languageDetector)
    {
        _github = github ?? throw new ArgumentNullException(nameof(github));
        _reviewer = reviewer ?? throw new ArgumentNullException(nameof(reviewer));
        _languageDetector = languageDetector ?? throw new ArgumentNullException(nameof(languageDetector));
    }

    public async Task<(PullRequestData PullRequest, PullRequestReviewResult Review)> ReviewAsync(
        PullRequestReference reference,
        CancellationToken cancellationToken = default)
    {
        var pullRequest = await _github.GetAsync(reference, cancellationToken);
        var changedFiles = UnifiedDiffParser.Parse(pullRequest.UnifiedDiff);
        var findings = new List<ReviewFinding>();
        var comments = new List<DraftReviewComment>();
        var usages = new List<ApiUsage>();
        string? model = null;

        foreach (var changedFile in changedFiles)
        {
            if (!_languageDetector.TryDetect(changedFile.Path, out var language) || language is null)
            {
                continue;
            }

            var content = string.Join(Environment.NewLine, changedFile.Lines.Select(line => line.Content));
            var source = new SourceFile(changedFile.Path, changedFile.Path, content, language);
            var result = await _reviewer.ReviewAsync(source, cancellationToken);
            model ??= result.Model;
            if (result.Usage is not null)
            {
                usages.Add(result.Usage);
            }

            foreach (var finding in result.Findings)
            {
                var normalized = finding with { File = changedFile.Path };
                findings.Add(normalized);
                var sourceIndex = TryExtractLine(finding.Location);
                if (sourceIndex is > 0 && sourceIndex <= changedFile.Lines.Count)
                {
                    var diffLine = changedFile.Lines[sourceIndex.Value - 1];
                    if (diffLine.IsAddition)
                    {
                        comments.Add(new DraftReviewComment(
                            changedFile.Path,
                            diffLine.NewLineNumber,
                            $"**{finding.Severity} — {finding.Category}**\n\n{finding.Explanation}\n\nSuggested improvement: {finding.SuggestedImprovement}"));
                    }
                }
            }
        }

        return (pullRequest, new PullRequestReviewResult(findings, comments, Aggregate(usages), model ?? "unavailable"));
    }

    private static int? TryExtractLine(string? location)
    {
        var match = LineNumber().Match(location ?? string.Empty);
        return match.Success && int.TryParse(match.Groups[1].Value, out var line) ? line : null;
    }

    private static ApiUsage? Aggregate(List<ApiUsage> usages) => usages.Count == 0
        ? null
        : new ApiUsage(usages.Sum(item => item.InputTokens ?? 0), usages.Sum(item => item.OutputTokens ?? 0), usages.Sum(item => item.TotalTokens ?? 0));

    [GeneratedRegex(@"\b(?:line\s*)?(\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LineNumber();
}
