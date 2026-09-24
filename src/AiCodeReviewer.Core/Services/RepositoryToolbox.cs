using System.Text.Json;
using System.Text.RegularExpressions;
using AiCodeReviewer.Core.Abstractions;
using AiCodeReviewer.Core.Exceptions;

namespace AiCodeReviewer.Core.Services;

public sealed partial class RepositoryToolbox : IRepositoryToolbox
{
    private readonly string _rootPath;
    private readonly int _maximumOutputCharacters;

    public RepositoryToolbox(string rootPath, int maximumOutputCharacters)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            throw new RepositoryException("A valid repository directory is required for repository tools.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maximumOutputCharacters, 500);
        _rootPath = Path.GetFullPath(rootPath);
        _maximumOutputCharacters = maximumOutputCharacters;
    }

    public string Execute(string toolName, string argumentsJson)
    {
        try
        {
            using var arguments = JsonDocument.Parse(argumentsJson);
            var output = toolName switch
            {
                "list_files" => ListFiles(arguments.RootElement),
                "read_file" => ReadFile(arguments.RootElement),
                "search_code" => SearchCode(arguments.RootElement, wholeWord: false),
                "find_symbol" => SearchCode(arguments.RootElement, wholeWord: true),
                "find_references" => SearchCode(arguments.RootElement, wholeWord: true),
                "inspect_project_structure" => InspectProjectStructure(),
                _ => JsonSerializer.Serialize(new { error = $"Unknown tool: {toolName}" })
            };
            return Truncate(output);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or IOException or UnauthorizedAccessException or RepositoryException)
        {
            return JsonSerializer.Serialize(new { error = exception.Message });
        }
    }

    private string ListFiles(JsonElement arguments)
    {
        var pattern = GetOptionalString(arguments, "pattern") ?? string.Empty;
        var files = new RepositoryScanner().Scan(_rootPath).Files
            .Select(file => file.RelativePath)
            .Where(path => string.IsNullOrWhiteSpace(pattern) || path.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            .Take(500)
            .ToArray();
        return JsonSerializer.Serialize(new { files });
    }

    private string ReadFile(JsonElement arguments)
    {
        var relativePath = GetRequiredString(arguments, "path");
        var startLine = GetOptionalInt(arguments, "startLine") ?? 1;
        var endLine = GetOptionalInt(arguments, "endLine") ?? startLine + 199;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(startLine);
        ArgumentOutOfRangeException.ThrowIfLessThan(endLine, startLine);
        endLine = Math.Min(endLine, startLine + 499);

        var fullPath = ResolveSafePath(relativePath);
        var lines = File.ReadLines(fullPath).Skip(startLine - 1).Take(endLine - startLine + 1);
        var numbered = lines.Select((line, index) => $"{startLine + index,5}: {line}");
        return JsonSerializer.Serialize(new { path = relativePath, startLine, endLine, content = string.Join(Environment.NewLine, numbered) });
    }

    private string SearchCode(JsonElement arguments, bool wholeWord)
    {
        var query = GetOptionalString(arguments, "query") ?? GetRequiredString(arguments, "symbol");
        if (query.Length > 200)
        {
            throw new ArgumentException("Search text cannot exceed 200 characters.");
        }

        var regex = wholeWord
            ? new Regex($@"\b{Regex.Escape(query)}\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))
            : null;
        var results = new List<object>();
        foreach (var file in new RepositoryScanner().Scan(_rootPath).Files)
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(file.FullPath))
            {
                lineNumber++;
                if ((regex?.IsMatch(line) ?? line.Contains(query, StringComparison.OrdinalIgnoreCase)))
                {
                    results.Add(new { file = file.RelativePath, line = lineNumber, text = line.Trim() });
                    if (results.Count >= 100)
                    {
                        return JsonSerializer.Serialize(new { results, truncated = true });
                    }
                }
            }
        }

        return JsonSerializer.Serialize(new { results, truncated = false });
    }

    private string InspectProjectStructure()
    {
        var scan = new RepositoryScanner().Scan(_rootPath);
        var byLanguage = scan.Files
            .GroupBy(file => file.Language.DisplayName)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var topDirectories = scan.Files
            .Select(file => file.RelativePath.Split('/')[0])
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Take(30)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        return JsonSerializer.Serialize(new { root = _rootPath, supportedFiles = scan.Files.Count, byLanguage, topDirectories });
    }

    private string ResolveSafePath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new RepositoryException("Tool paths must be repository-relative.");
        }

        var resolved = Path.GetFullPath(Path.Combine(_rootPath, relativePath));
        var rootPrefix = _rootPath.EndsWith(Path.DirectorySeparatorChar) ? _rootPath : _rootPath + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(resolved))
        {
            throw new RepositoryException("The requested file is outside the repository or does not exist.");
        }

        return resolved;
    }

    private string Truncate(string value) => value.Length <= _maximumOutputCharacters
        ? value
        : value[.._maximumOutputCharacters] + "\n[tool output truncated]";

    private static string GetRequiredString(JsonElement arguments, string name) =>
        GetOptionalString(arguments, name) ?? throw new ArgumentException($"Tool argument '{name}' is required.");

    private static string? GetOptionalString(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? GetOptionalInt(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;
}
