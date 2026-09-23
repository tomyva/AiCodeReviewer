using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Core.Services;

public static class ReviewPromptFactory
{
    public static ReviewPrompt Create(SourceFile sourceFile)
    {
        ArgumentNullException.ThrowIfNull(sourceFile);

        const string instructions = """
            You are a careful senior C# code reviewer. Review only the supplied file and report concrete,
            actionable findings. Check for bugs and logic errors, maintainability problems, performance
            concerns, security concerns, poor coding practices, refactoring opportunities, missing error
            handling, and testing opportunities. Do not invent surrounding repository context. Prefer precise
            line ranges or code identifiers for location. Avoid cosmetic findings unless they materially affect
            clarity or correctness. Return only data matching the supplied JSON schema. If there are no useful
            findings, return an empty findings array.
            """;

        var numberedCode = string.Join(
            Environment.NewLine,
            sourceFile.Content
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n')
                .Select((line, index) => $"{index + 1,5}: {line}"));

        var input = $"""
            Review this C# source file.

            File: {sourceFile.Name}

            ```csharp
            {numberedCode}
            ```
            """;

        return new ReviewPrompt(instructions, input);
    }
}
