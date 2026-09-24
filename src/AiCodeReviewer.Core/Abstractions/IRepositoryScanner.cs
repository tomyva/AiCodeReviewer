using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Core.Abstractions;

public interface IRepositoryScanner
{
    RepositoryScanResult Scan(string rootPath);
}
