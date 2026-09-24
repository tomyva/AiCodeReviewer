using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.GitHub;

public sealed record PullRequestReference(string Owner, string Repository, int Number)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Owner) || string.IsNullOrWhiteSpace(Repository))
        {
            throw new ArgumentException("GitHub owner and repository are required.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Number);
    }
}

public sealed record PullRequestData(PullRequestReference Reference, string HeadSha, string UnifiedDiff);

public sealed record DiffLine(int NewLineNumber, string Content, bool IsAddition);

public sealed record ChangedFile(string Path, IReadOnlyList<DiffLine> Lines);

public sealed record DraftReviewComment(string Path, int Line, string Body);

public sealed record PullRequestReviewResult(
    IReadOnlyList<ReviewFinding> Findings,
    IReadOnlyList<DraftReviewComment> DraftComments,
    ApiUsage? Usage,
    string Model);
