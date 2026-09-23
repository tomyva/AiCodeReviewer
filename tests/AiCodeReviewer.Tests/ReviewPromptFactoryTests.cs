using AiCodeReviewer.Core.Models;
using AiCodeReviewer.Core.Services;

namespace AiCodeReviewer.Tests;

public sealed class ReviewPromptFactoryTests
{
    [Fact]
    public void CreateIncludesFileNameAndNumberedSource()
    {
        var sourceFile = new SourceFile("C:/Example.cs", "Example.cs", "first line\nsecond line");
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
        var sourceFile = new SourceFile("C:/Example.cs", "Example.cs", "class Example {}");
        var prompt = ReviewPromptFactory.Create(sourceFile);
        Assert.Contains(reviewArea, prompt.Instructions, StringComparison.OrdinalIgnoreCase);
    }
}
