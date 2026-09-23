namespace AiCodeReviewer.Core.Exceptions;

public sealed class CodeReviewServiceException : Exception
{
    public CodeReviewServiceException(string message) : base(message)
    {
    }

    public CodeReviewServiceException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
