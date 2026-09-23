using AiCodeReviewer.Core.Exceptions;
using AiCodeReviewer.Core.Models;
using AiCodeReviewer.Core.Services;
using AiCodeReviewer.OpenAI;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    if (args.Length > 1 || args is ["--help"] or ["-h"])
    {
        PrintUsage();
        return args.Length > 1 ? 2 : 0;
    }

    var filePath = args.Length == 1 ? args[0] : PromptForPath();
    if (string.IsNullOrWhiteSpace(filePath))
    {
        Console.Error.WriteLine("Error: a source file path is required.");
        return 2;
    }

    try
    {
        var sourceFile = await new SourceFileLoader().LoadAsync(filePath);
        var options = OpenAiOptions.FromEnvironment();
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var reviewer = new OpenAiCodeReviewService(httpClient, options);

        Console.WriteLine($"Reviewing {sourceFile.Name} with {options.Model}...");
        var result = await reviewer.ReviewAsync(sourceFile);
        PrintResult(result);
        return 0;
    }
    catch (SourceFileException exception)
    {
        Console.Error.WriteLine($"File error: {exception.Message}");
        return 2;
    }
    catch (InvalidOperationException exception)
    {
        Console.Error.WriteLine($"Configuration error: {exception.Message}");
        return 2;
    }
    catch (CodeReviewServiceException exception)
    {
        Console.Error.WriteLine($"Review failed: {exception.Message}");
        return 1;
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("Review cancelled.");
        return 1;
    }
}

static string? PromptForPath()
{
    Console.Write("C# source file path: ");
    return Console.ReadLine()?.Trim().Trim('"');
}

static void PrintResult(CodeReviewResult result)
{
    Console.WriteLine();
    if (result.Findings.Count == 0)
    {
        Console.WriteLine("No actionable findings were returned.");
    }
    else
    {
        Console.WriteLine($"Findings ({result.Findings.Count}):");
        for (var index = 0; index < result.Findings.Count; index++)
        {
            var finding = result.Findings[index];
            Console.WriteLine();
            Console.WriteLine($"{index + 1}. [{finding.Severity}] {finding.Category}");
            if (!string.IsNullOrWhiteSpace(finding.Location))
            {
                Console.WriteLine($"   Location: {finding.Location}");
            }

            Console.WriteLine($"   {finding.Explanation}");
            Console.WriteLine($"   Suggested improvement: {finding.SuggestedImprovement}");
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Model: {result.Model}");
    if (result.Usage is { } usage)
    {
        Console.WriteLine($"Token usage: input {FormatTokenCount(usage.InputTokens)}, output {FormatTokenCount(usage.OutputTokens)}, total {FormatTokenCount(usage.TotalTokens)}");
    }
    else
    {
        Console.WriteLine("Token usage: unavailable");
    }
}

static string FormatTokenCount(int? count) => count?.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) ?? "unavailable";

static void PrintUsage()
{
    Console.WriteLine("Usage: dotnet run --project src/AiCodeReviewer.Cli -- <path-to-file.cs>");
    Console.WriteLine("If no path is supplied, the application prompts for one.");
}
