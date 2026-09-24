using AiCodeReviewer.Core.Models;
using AiCodeReviewer.Core.Services;

namespace AiCodeReviewer.Tests;

public sealed class ReviewPromptFactoryTests
{
    [Fact]
    public void CreateIncludesFileNameAndNumberedSource()
    {
        var sourceFile = CreateSourceFile("Example.cs", "first line\nsecond line");
        var prompt = ReviewPromptFactory.Create(sourceFile);
        Assert.Contains("File: Example.cs", prompt.Input);
        Assert.Contains("    1: first line", prompt.Input);
        Assert.Contains("    2: second line", prompt.Input);
    }

    [Theory]
    [InlineData("bugs and logic errors")]
    [InlineData("security concerns")]
    [InlineData("testing opportunities")]
    public void CreateIncludesRequiredReviewArea(string reviewArea)
    {
        var sourceFile = CreateSourceFile("Example.cs", "class Example {}");
        var prompt = ReviewPromptFactory.Create(sourceFile);
        Assert.Contains(reviewArea, prompt.Instructions, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateAdaptsInstructionsAndFenceToLanguage()
    {
        var sourceFile = CreateSourceFile("worker.py", "async def run():\n    pass");

        var prompt = ReviewPromptFactory.Create(sourceFile);

        Assert.Contains("senior Python code reviewer", prompt.Instructions);
        Assert.Contains("mutable defaults", prompt.Instructions);
        Assert.Contains("Review this Python source file", prompt.Input);
        Assert.Contains("```python", prompt.Input);
    }

    private static SourceFile CreateSourceFile(string name, string content)
    {
        var language = new LanguageDetector().Detect(name);
        return new SourceFile($"C:/{name}", name, content, language);
    }
}
