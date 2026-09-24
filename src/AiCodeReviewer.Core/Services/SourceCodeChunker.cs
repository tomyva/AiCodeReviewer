using System.Text;
using AiCodeReviewer.Core.Abstractions;
using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Core.Services;

public sealed class SourceCodeChunker : ICodeChunker
{
    private readonly int _maximumCharacters;
    private readonly int _overlapLines;

    public SourceCodeChunker(int maximumCharacters = 3_000, int overlapLines = 3)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCharacters, 100);
        ArgumentOutOfRangeException.ThrowIfNegative(overlapLines);
        _maximumCharacters = maximumCharacters;
        _overlapLines = overlapLines;
    }

    public IReadOnlyList<CodeChunk> Chunk(SourceFile sourceFile)
    {
        ArgumentNullException.ThrowIfNull(sourceFile);
        var lines = sourceFile.Content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var chunks = new List<CodeChunk>();
        var start = 0;

        while (start < lines.Length)
        {
            var builder = new StringBuilder();
            var end = start;
            while (end < lines.Length)
            {
                var additionalLength = lines[end].Length + Environment.NewLine.Length;
                if (builder.Length > 0 && builder.Length + additionalLength > _maximumCharacters)
                {
                    break;
                }

                builder.AppendLine(lines[end]);
                end++;

                if (builder.Length >= _maximumCharacters * 3 / 4 && end < lines.Length &&
                    string.IsNullOrWhiteSpace(lines[end]))
                {
                    break;
                }
            }

            chunks.Add(new CodeChunk(
                sourceFile.Name,
                start + 1,
                end,
                builder.ToString().TrimEnd(),
                sourceFile.Language));

            if (end >= lines.Length)
            {
                break;
            }

            start = Math.Max(start + 1, end - _overlapLines);
        }

        return chunks;
    }
}
