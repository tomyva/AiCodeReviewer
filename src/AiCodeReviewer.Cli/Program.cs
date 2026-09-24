using System.Globalization;
using AiCodeReviewer.Application;
using AiCodeReviewer.Core.Models;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    if (args is ["--help"] or ["-h"])
    {
        PrintHelp();
        return 0;
    }

    try
    {
        var (request, publish) = ParseRequest(args);
        using var runtime = new ApplicationRuntime();
        var progress = new Progress<ReviewProgress>(update => Console.WriteLine($"[{update.Stage}] {update.Message}"));
        var result = await runtime.Application.ReviewAsync(request, progress);
        if (!result.IsSuccess)
        {
            Console.Error.WriteLine($"{result.Error!.Code}: {result.Error.Message}");
            return result.Error.Code is ApplicationErrorCode.Validation or ApplicationErrorCode.Configuration or ApplicationErrorCode.File ? 2 : 1;
        }

        var report = result.Value!;
        PrintReport(report);
        if (publish)
        {
            if (report.PublicationDraft is null)
            {
                Console.Error.WriteLine("No pull-request review draft is available to publish.");
                return 1;
            }

            var published = await runtime.Application.PublishPullRequestReviewAsync(new PublishReviewRequest(report.PublicationDraft, true));
            if (!published.IsSuccess)
            {
                Console.Error.WriteLine($"{published.Error!.Code}: {published.Error.Message}");
                return 1;
            }

            Console.WriteLine("Published the approved GitHub review.");
        }
        else if (report.PublicationDraft is not null)
        {
            Console.WriteLine("Dry run only. Review the drafts, then rerun with --approve to publish them.");
        }

        return report.Statistics is { FilesDiscovered: > 0, FilesReviewed: 0 } ? 1 : 0;
    }
    catch (ArgumentException exception)
    {
        Console.Error.WriteLine($"Argument error: {exception.Message}");
        return 2;
    }
}

static (ReviewRequest Request, bool Publish) ParseRequest(string[] args)
{
    if (args is ["review-repo", var repositoryPath, .. var repoOptions])
    {
        var (files, characters) = ParseRepositoryOptions(repoOptions);
        return (new ReviewRequest(ReviewMode.LocalRepository, Path: repositoryPath, MaximumFiles: files, MaximumCharacters: characters), false);
    }

    if (args is ["review-rag", var ragPath, .. var ragOptions])
    {
        var (files, characters) = ParseRepositoryOptions(ragOptions);
        return (new ReviewRequest(ReviewMode.RagRepository, Path: ragPath, MaximumFiles: files, MaximumCharacters: characters), false);
    }

    if (args is ["review-agent", var agentPath])
    {
        return (new ReviewRequest(ReviewMode.AgentRepository, Path: agentPath), false);
    }

    if (args is ["review-pr", var owner, var repository, var numberText, .. var approvalArguments])
    {
        if (!int.TryParse(numberText, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0)
        {
            throw new ArgumentException("The pull-request number must be a positive integer.");
        }

        if (approvalArguments.Length > 1 || approvalArguments.Any(argument => argument != "--approve"))
        {
            throw new ArgumentException("The only supported pull-request option is --approve.");
        }

        return (new ReviewRequest(ReviewMode.GitHubPullRequest, GitHubOwner: owner, GitHubRepository: repository, PullRequestNumber: number), approvalArguments.Contains("--approve", StringComparer.Ordinal));
    }

    if (args is ["review-specialists", var specialistPath])
    {
        return (new ReviewRequest(ReviewMode.Specialists, Path: specialistPath), false);
    }

    if (args is ["review-remote", var remoteUrl, .. var remoteOptions])
    {
        var (files, characters) = ParseRepositoryOptions(remoteOptions);
        return (new ReviewRequest(ReviewMode.RemoteRepository, RemoteUrl: remoteUrl, MaximumFiles: files, MaximumCharacters: characters), false);
    }

    if (args is ["review-file", var sourcePath])
    {
        return (new ReviewRequest(ReviewMode.File, Path: sourcePath), false);
    }

    if (args.Length == 1)
    {
        return (new ReviewRequest(ReviewMode.File, Path: args[0]), false);
    }

    if (args.Length == 0)
    {
        Console.Write("Source file path: ");
        var path = Console.ReadLine()?.Trim().Trim('"');
        return (new ReviewRequest(ReviewMode.File, Path: path), false);
    }

    throw new ArgumentException("The command is not recognized. Run with --help to see the available commands.");
}

static (int MaximumFiles, long MaximumCharacters) ParseRepositoryOptions(string[] arguments)
{
    var maximumFiles = 50;
    long maximumCharacters = 250_000;
    for (var index = 0; index < arguments.Length; index += 2)
    {
        if (index + 1 >= arguments.Length)
        {
            throw new ArgumentException($"A value is required for {arguments[index]}.");
        }

        switch (arguments[index])
        {
            case "--max-files" when int.TryParse(arguments[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var files) && files > 0:
                maximumFiles = files;
                break;
            case "--max-chars" when long.TryParse(arguments[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var characters) && characters > 0:
                maximumCharacters = characters;
                break;
            case "--max-files":
            case "--max-chars":
                throw new ArgumentException($"The value for {arguments[index]} must be a positive integer.");
            default:
                throw new ArgumentException($"Unknown repository option: {arguments[index]}");
        }
    }

    return (maximumFiles, maximumCharacters);
}

static void PrintReport(ReviewReport report)
{
    PrintFindings(report.Findings);
    if (report.Statistics is not null)
    {
        Console.WriteLine();
        Console.WriteLine("Repository statistics:");
        Console.WriteLine($"  Files discovered: {report.Statistics.FilesDiscovered:N0}");
        Console.WriteLine($"  Files reviewed:   {report.Statistics.FilesReviewed:N0}");
        Console.WriteLine($"  Files skipped:    {report.Statistics.FilesSkipped:N0}");
        Console.WriteLine($"  Files failed:     {report.Statistics.FilesFailed:N0}");
        Console.WriteLine($"  Characters sent:  {report.Statistics.CharactersReviewed:N0}");
        Console.WriteLine($"  Chunks indexed:   {report.Statistics.ChunksIndexed:N0}");
        Console.WriteLine($"  Context retrieved:{report.Statistics.ContextChunksRetrieved,10:N0}");
    }

    if (report.Investigation.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Investigation steps:");
        foreach (var step in report.Investigation)
        {
            Console.WriteLine($"  {step.Number}. {step.Tool} {step.Arguments} -> {step.OutputCharacters:N0} characters");
        }
    }

    if (report.PublicationDraft is not null)
    {
        Console.WriteLine();
        Console.WriteLine($"Draft inline comments: {report.PublicationDraft.Comments.Count}");
        foreach (var comment in report.PublicationDraft.Comments)
        {
            Console.WriteLine($"  {comment.Path}:{comment.Line} — {comment.Body.Replace(Environment.NewLine, " ", StringComparison.Ordinal)}");
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Model: {report.Model}");
    Console.WriteLine(report.Usage is null
        ? "Token usage: unavailable"
        : $"Token usage: input {Format(report.Usage.InputTokens)}, output {Format(report.Usage.OutputTokens)}, total {Format(report.Usage.TotalTokens)}");
}

static void PrintFindings(IReadOnlyList<ReviewFinding> findings)
{
    Console.WriteLine();
    if (findings.Count == 0)
    {
        Console.WriteLine("No actionable findings were returned.");
        return;
    }

    Console.WriteLine($"Findings ({findings.Count}):");
    var index = 1;
    foreach (var finding in findings.OrderByDescending(item => item.Severity).ThenBy(item => item.File, StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine($"  {index++}. [{finding.Severity}] {finding.Category} — {finding.File} {finding.Location}");
        Console.WriteLine($"     {finding.Explanation}");
        Console.WriteLine($"     Suggested improvement: {finding.SuggestedImprovement}");
    }
}

static string Format(int? value) => value?.ToString("N0", CultureInfo.InvariantCulture) ?? "unavailable";

static void PrintHelp()
{
    Console.WriteLine("AI Code Reviewer commands:");
    Console.WriteLine("  review-file <path>");
    Console.WriteLine("  review-repo <directory> [--max-files N] [--max-chars N]");
    Console.WriteLine("  review-remote <https-git-url> [--max-files N] [--max-chars N]");
    Console.WriteLine("  review-rag <directory> [--max-files N] [--max-chars N]");
    Console.WriteLine("  review-agent <directory>");
    Console.WriteLine("  review-pr <owner> <repo> <number> [--approve]");
    Console.WriteLine("  review-specialists <path>");
}
