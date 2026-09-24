using AiCodeReviewer.GitHub;

namespace AiCodeReviewer.Tests;

public sealed class UnifiedDiffParserTests
{
    private const string Diff = """
        diff --git a/src/Example.cs b/src/Example.cs
        index 111..222 100644
        --- a/src/Example.cs
        +++ b/src/Example.cs
        @@ -10,3 +10,4 @@
         class Example
         {
        +    public int Value => 42;
         }
        """;

    [Fact]
    public void ParseExtractsChangedFileAndNewLineNumbers()
    {
        var file = Assert.Single(UnifiedDiffParser.Parse(Diff));

        Assert.Equal("src/Example.cs", file.Path);
        var addition = Assert.Single(file.Lines.Where(line => line.IsAddition));
        Assert.Equal(12, addition.NewLineNumber);
        Assert.Contains("Value", addition.Content);
    }
}
