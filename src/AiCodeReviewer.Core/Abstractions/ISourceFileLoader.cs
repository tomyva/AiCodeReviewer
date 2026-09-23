using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Core.Abstractions;

public interface ISourceFileLoader
{
    Task<SourceFile> LoadAsync(string path, CancellationToken cancellationToken = default);
}
