using System.Text;
using AiCodeReviewer.Core.Abstractions;
using AiCodeReviewer.Core.Exceptions;
using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Core.Services;

public sealed class SourceFileLoader : ISourceFileLoader
{
    public const long DefaultMaximumFileSizeBytes = 512 * 1024;
    private readonly long _maximumFileSizeBytes;
    private readonly ILanguageDetector _languageDetector;

    public SourceFileLoader(
        long maximumFileSizeBytes = DefaultMaximumFileSizeBytes,
        ILanguageDetector? languageDetector = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFileSizeBytes);
        _maximumFileSizeBytes = maximumFileSizeBytes;
        _languageDetector = languageDetector ?? new LanguageDetector();
    }

    public async Task<SourceFile> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new SourceFileException("A C# source file path is required.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new SourceFileException("The source file path is invalid.", exception);
        }

        var language = _languageDetector.Detect(fullPath);

        if (!File.Exists(fullPath))
        {
            throw new SourceFileException($"The source file does not exist: {fullPath}");
        }

        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Length == 0)
        {
            throw new SourceFileException("The source file is empty.");
        }

        if (fileInfo.Length > _maximumFileSizeBytes)
        {
            throw new SourceFileException(
                $"The source file is too large ({fileInfo.Length:N0} bytes). The limit is {_maximumFileSizeBytes:N0} bytes.");
        }

        try
        {
            var content = await File.ReadAllTextAsync(fullPath, new UTF8Encoding(false, true), cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new SourceFileException("The source file contains no reviewable code.");
            }

            return new SourceFile(fullPath, Path.GetFileName(fullPath), content, language);
        }
        catch (DecoderFallbackException exception)
        {
            throw new SourceFileException("The source file is not valid UTF-8 text.", exception);
        }
        catch (IOException exception)
        {
            throw new SourceFileException("The source file could not be read.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new SourceFileException("Access to the source file was denied.", exception);
        }
    }
}
