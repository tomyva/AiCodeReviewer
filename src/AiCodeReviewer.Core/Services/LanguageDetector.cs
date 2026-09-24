using AiCodeReviewer.Core.Abstractions;
using AiCodeReviewer.Core.Exceptions;
using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Core.Services;

public sealed class LanguageDetector : ILanguageDetector
{
    private static readonly LanguageProfile CSharp = new(
        ProgrammingLanguage.CSharp,
        "C#",
        "csharp",
        "Pay particular attention to .NET idioms, nullable reference types, async/await, disposal, exceptions, and thread safety.");

    private static readonly LanguageProfile Python = new(
        ProgrammingLanguage.Python,
        "Python",
        "python",
        "Pay particular attention to Python's dynamic typing, mutable defaults, iteration behavior, resource management, exceptions, and async code.");

    private static readonly LanguageProfile JavaScript = new(
        ProgrammingLanguage.JavaScript,
        "JavaScript",
        "javascript",
        "Pay particular attention to coercion, null/undefined, promises, async control flow, closures, runtime validation, and browser or Node.js boundaries.");

    private static readonly LanguageProfile TypeScript = new(
        ProgrammingLanguage.TypeScript,
        "TypeScript",
        "typescript",
        "Pay particular attention to type narrowing, unsafe assertions, null/undefined, promises, async control flow, runtime validation, and emitted JavaScript behavior.");

    private static readonly LanguageProfile Java = new(
        ProgrammingLanguage.Java,
        "Java",
        "java",
        "Pay particular attention to nullability, exceptions, resource management, concurrency, collection behavior, generics, and JVM performance implications.");

    private static readonly LanguageProfile C = new(
        ProgrammingLanguage.C,
        "C",
        "c",
        "Pay particular attention to bounds, allocation and deallocation, pointer validity, integer overflow, undefined behavior, error codes, and ownership.");

    private static readonly LanguageProfile Cpp = new(
        ProgrammingLanguage.Cpp,
        "C++",
        "cpp",
        "Pay particular attention to object lifetime, ownership, RAII, bounds, iterator validity, undefined behavior, exception safety, and concurrency.");

    private static readonly Dictionary<string, LanguageProfile> Profiles =
        new Dictionary<string, LanguageProfile>(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"] = CSharp,
            [".py"] = Python,
            [".js"] = JavaScript,
            [".jsx"] = JavaScript,
            [".mjs"] = JavaScript,
            [".cjs"] = JavaScript,
            [".ts"] = TypeScript,
            [".tsx"] = TypeScript,
            [".mts"] = TypeScript,
            [".cts"] = TypeScript,
            [".java"] = Java,
            [".c"] = C,
            [".h"] = C,
            [".cc"] = Cpp,
            [".cpp"] = Cpp,
            [".cxx"] = Cpp,
            [".hh"] = Cpp,
            [".hpp"] = Cpp,
            [".hxx"] = Cpp
        };

    public bool TryDetect(string fileName, out LanguageProfile? language)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            language = null;
            return false;
        }

        return Profiles.TryGetValue(Path.GetExtension(fileName), out language);
    }

    public LanguageProfile Detect(string fileName)
    {
        if (TryDetect(fileName, out var language) && language is not null)
        {
            return language;
        }

        var extension = Path.GetExtension(fileName);
        var description = string.IsNullOrWhiteSpace(extension) ? "files without an extension" : $"'{extension}' files";
        throw new SourceFileException($"Version 2 does not support {description}.");
    }
}
