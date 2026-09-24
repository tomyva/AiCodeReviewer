namespace AiCodeReviewer.Core.Models;

public sealed record RepositoryReviewOptions(int MaximumFiles = 50, long MaximumTotalCharacters = 250_000)
{
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumFiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumTotalCharacters);
    }
}
