namespace AiCodeReviewer.Core.Exceptions;

public sealed class SourceFileException : Exception
{
    public SourceFileException(string message) : base(message)
    {
    }

    public SourceFileException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
