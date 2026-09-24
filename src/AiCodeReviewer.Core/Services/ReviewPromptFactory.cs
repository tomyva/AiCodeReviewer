using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Core.Services;

public static class ReviewPromptFactory
{
    public static ReviewPrompt Create(
        SourceFile sourceFile,
        IReadOnlyList<CodeChunk>? relatedContext = null,
        string? specialistGuidance = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFile);

        var instructions = $$"""
            You are a careful senior {{sourceFile.Language.DisplayName}} code reviewer. Review only the supplied file and report concrete,
            actionable findings. Check for bugs and logic errors, maintainability problems, performance
            concerns, security concerns, poor coding practices, refactoring opportunities, missing error
            handling, and testing opportunities. Do not invent surrounding repository context. Prefer precise
            line ranges or code identifiers for location. Avoid cosmetic findings unless they materially affect
            clarity or correctness. Return only data matching the supplied JSON schema. If there are no useful
            findings, return an empty findings array.

            Language-specific guidance: {{sourceFile.Language.ReviewGuidance}}

            {{(string.IsNullOrWhiteSpace(specialistGuidance) ? string.Empty : $"Specialist remit: {specialistGuidance} Do not duplicate generic observations outside this remit.")}}
            """;

        var numberedCode = string.Join(
            Environment.NewLine,
            sourceFile.Content
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n')
                .Select((line, index) => $"{index + 1,5}: {line}"));

        var context = FormatRelatedContext(relatedContext);
        var input = $"""
            Review this {sourceFile.Language.DisplayName} source file.

            File: {sourceFile.Name}

            ```{sourceFile.Language.CodeFence}
            {numberedCode}
            ```

            {context}
            """;

        return new ReviewPrompt(instructions, input);
    }

    private static string FormatRelatedContext(IReadOnlyList<CodeChunk>? relatedContext)
    {
        if (relatedContext is null || relatedContext.Count == 0)
        {
            return "No related repository context was retrieved.";
        }

        var sections = relatedContext.Select(chunk => $$"""
            Related context only — do not report findings against this file unless it directly affects the target:
            File: {{chunk.File}}, lines {{chunk.StartLine}}-{{chunk.EndLine}}
            ```{{chunk.Language.CodeFence}}
            {{chunk.Content}}
            ```
            """);
        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }
}
