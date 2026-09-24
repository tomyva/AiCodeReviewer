using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Core.Abstractions;

public interface ILanguageDetector
{
    bool TryDetect(string fileName, out LanguageProfile? language);

    LanguageProfile Detect(string fileName);
}
