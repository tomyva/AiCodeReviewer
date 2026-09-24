using AiCodeReviewer.Core.Abstractions;
using AiCodeReviewer.Core.Exceptions;
using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Core.Services;

public sealed class RepositoryScanner : IRepositoryScanner
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", ".vs", ".idea", ".vscode", "bin", "obj", "packages",
        "node_modules", "vendor", "dist", "build", "out", "target", "coverage", ".next",
        ".nuxt", ".cache", "artifacts", "generated"
    };

    private static readonly string[] IgnoredFileSuffixes =
    [
        ".g.cs", ".g.i.cs", ".designer.cs", ".generated.cs", ".min.js", ".min.css", ".map"
    ];

    private readonly ILanguageDetector _languageDetector;

    public RepositoryScanner(ILanguageDetector? languageDetector = null) =>
        _languageDetector = languageDetector ?? new LanguageDetector();

    public RepositoryScanResult Scan(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new RepositoryException("A repository directory is required.");
        }

        var fullRootPath = Path.GetFullPath(rootPath);
        if (!Directory.Exists(fullRootPath))
        {
            throw new RepositoryException($"The repository directory does not exist: {fullRootPath}");
        }

        var files = new List<RepositoryFile>();
        var discovered = 0;
        var skipped = 0;
        var pending = new Stack<string>();
        pending.Push(fullRootPath);

        try
        {
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                foreach (var childDirectory in Directory.EnumerateDirectories(directory))
                {
                    var info = new DirectoryInfo(childDirectory);
                    if (IgnoredDirectories.Contains(info.Name) || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        continue;
                    }

                    pending.Push(childDirectory);
                }

                foreach (var path in Directory.EnumerateFiles(directory))
                {
                    discovered++;
                    var info = new FileInfo(path);
                    if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || IsIgnoredFile(info.Name) ||
                        !_languageDetector.TryDetect(info.Name, out var language) || language is null)
                    {
                        skipped++;
                        continue;
                    }

                    files.Add(new RepositoryFile(
                        info.FullName,
                        Path.GetRelativePath(fullRootPath, info.FullName).Replace('\\', '/'),
                        info.Length,
                        language));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new RepositoryException("The repository could not be scanned completely.", exception);
        }

        files.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath));
        return new RepositoryScanResult(fullRootPath, files, discovered, skipped);
    }

    private static bool IsIgnoredFile(string fileName) =>
        IgnoredFileSuffixes.Any(suffix => fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
}
