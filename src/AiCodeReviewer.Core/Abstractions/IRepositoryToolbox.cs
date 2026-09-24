namespace AiCodeReviewer.Core.Abstractions;

public interface IRepositoryToolbox
{
    string Execute(string toolName, string argumentsJson);
}
