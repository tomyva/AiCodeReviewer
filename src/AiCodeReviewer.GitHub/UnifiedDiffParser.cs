using System.Text.RegularExpressions;

namespace AiCodeReviewer.GitHub;

public static partial class UnifiedDiffParser
{
    public static IReadOnlyList<ChangedFile> Parse(string unifiedDiff)
    {
        ArgumentNullException.ThrowIfNull(unifiedDiff);
        var files = new List<ChangedFile>();
        string? currentPath = null;
        List<DiffLine>? currentLines = null;
        var newLine = 0;

        foreach (var line in unifiedDiff.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                AddCurrent(files, currentPath, currentLines);
                currentPath = null;
                currentLines = [];
                continue;
            }

            if (line.StartsWith("+++ b/", StringComparison.Ordinal))
            {
                currentPath = line[6..];
                continue;
            }

            var match = HunkHeader().Match(line);
            if (match.Success)
            {
                newLine = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                continue;
            }

            if (currentLines is null || currentPath is null || line.StartsWith("---", StringComparison.Ordinal) || line.StartsWith("+++", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith('+'))
            {
                currentLines.Add(new DiffLine(newLine++, line[1..], IsAddition: true));
            }
            else if (!line.StartsWith('-') && !line.StartsWith('\\'))
            {
                currentLines.Add(new DiffLine(newLine++, line.Length > 0 ? line[1..] : string.Empty, IsAddition: false));
            }
        }

        AddCurrent(files, currentPath, currentLines);
        return files;
    }

    private static void AddCurrent(List<ChangedFile> files, string? path, List<DiffLine>? lines)
    {
        if (!string.IsNullOrWhiteSpace(path) && lines is { Count: > 0 })
        {
            files.Add(new ChangedFile(path, lines));
        }
    }

    [GeneratedRegex(@"^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeader();
}
