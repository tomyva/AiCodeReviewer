namespace AiCodeReviewer.Core.Models;

public sealed record AgentReviewOptions(int MaximumToolCalls = 12, int MaximumToolOutputCharacters = 12_000)
{
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumToolCalls);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumToolOutputCharacters, 500);
    }
}
