using AiCodeReviewer.Core.Exceptions;
using AiCodeReviewer.Core.Models;
using AiCodeReviewer.Core.Services;

namespace AiCodeReviewer.Tests;

public sealed class LanguageDetectorTests
{
    public static TheoryData<string, ProgrammingLanguage> SupportedFiles => new()
    {
        { "Example.cs", ProgrammingLanguage.CSharp },
        { "script.PY", ProgrammingLanguage.Python },
        { "app.js", ProgrammingLanguage.JavaScript },
        { "component.jsx", ProgrammingLanguage.JavaScript },
        { "server.ts", ProgrammingLanguage.TypeScript },
        { "component.tsx", ProgrammingLanguage.TypeScript },
        { "Service.java", ProgrammingLanguage.Java },
        { "library.c", ProgrammingLanguage.C },
        { "library.h", ProgrammingLanguage.C },
        { "engine.cpp", ProgrammingLanguage.Cpp },
        { "engine.hpp", ProgrammingLanguage.Cpp }
    };

    [Theory]
    [MemberData(nameof(SupportedFiles))]
    public void DetectReturnsExpectedLanguage(string fileName, ProgrammingLanguage expected)
    {
        var actual = new LanguageDetector().Detect(fileName);
        Assert.Equal(expected, actual.Language);
    }

    [Fact]
    public void DetectRejectsUnknownExtension()
    {
        var exception = Assert.Throws<SourceFileException>(() => new LanguageDetector().Detect("notes.txt"));
        Assert.Contains("does not support", exception.Message);
    }

    [Fact]
    public void TryDetectReturnsFalseForMissingExtension()
    {
        var detected = new LanguageDetector().TryDetect("Dockerfile", out var language);
        Assert.False(detected);
        Assert.Null(language);
    }
}
