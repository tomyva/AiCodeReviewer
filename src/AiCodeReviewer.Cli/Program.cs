using System.Globalization;
using AiCodeReviewer.Core.Exceptions;
using AiCodeReviewer.Core.Models;
using AiCodeReviewer.Core.Services;
using AiCodeReviewer.OpenAI;
using AiCodeReviewer.GitHub;

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
        if (args is ["review-repo", var repositoryPath, .. var optionArguments])
        {
            return await ReviewRepositoryAsync(repositoryPath, optionArguments);
        }

        if (args is ["review-rag", var ragRepositoryPath, .. var ragOptionArguments])
        {
            return await ReviewRepositoryWithRagAsync(ragRepositoryPath, ragOptionArguments);
        }

        if (args is ["review-agent", var agentRepositoryPath])
        {
            return await ReviewRepositoryWithAgentAsync(agentRepositoryPath);
        }

        if (args is ["review-pr", var owner, var repository, var pullNumberText, .. var approvalArguments])
        {
            return await ReviewPullRequestAsync(owner, repository, pullNumberText, approvalArguments);
        }

        if (args is ["review-specialists", var specialistSourcePath])
        {
            return await ReviewWithSpecialistsAsync(specialistSourcePath);
        }

        if (args is ["review-remote", var remoteUrl, .. var remoteOptions])
        {
            return await ReviewRemoteRepositoryAsync(remoteUrl, remoteOptions);
        }

        if (args is ["review-file", var sourcePath])
        {
            return await ReviewFileAsync(sourcePath);
        }

        if (args.Length == 1)
        {
            return await ReviewFileAsync(args[0]);
        }

        if (args.Length == 0)
        {
            var path = PromptForPath();
            return string.IsNullOrWhiteSpace(path) ? ShowMissingPathError() : await ReviewFileAsync(path);
        }

        PrintHelp();
        return 2;
    }
    catch (SourceFileException exception)
    {
        Console.Error.WriteLine($"File error: {exception.Message}");
        return 2;
    }
    catch (RepositoryException exception)
    {
        Console.Error.WriteLine($"Repository error: {exception.Message}");
        return 2;
    }
    catch (ArgumentException exception)
    {
        Console.Error.WriteLine($"Argument error: {exception.Message}");
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

static async Task<int> ReviewFileAsync(string filePath)
{
    var sourceFile = await new SourceFileLoader().LoadAsync(filePath);
    var options = OpenAiOptions.FromEnvironment();
    using var httpClient = CreateHttpClient();
    var reviewer = new OpenAiCodeReviewService(httpClient, options);

    Console.WriteLine($"Reviewing {sourceFile.Name} as {sourceFile.Language.DisplayName} with {options.Model}...");
    var result = await reviewer.ReviewAsync(sourceFile);
    PrintFindings(result.Findings);
    PrintApiUsage(result.Model, result.Usage);
    return 0;
}

static async Task<int> ReviewRepositoryAsync(string repositoryPath, string[] optionArguments)
{
    var reviewOptions = ParseRepositoryOptions(optionArguments);
    var providerOptions = OpenAiOptions.FromEnvironment();
    using var httpClient = CreateHttpClient();
    var fileReviewer = new OpenAiCodeReviewService(httpClient, providerOptions);
    var repositoryReviewer = new RepositoryReviewService(
        new RepositoryScanner(),
        new SourceFileLoader(),
        fileReviewer);

    Console.WriteLine($"Reviewing repository {Path.GetFullPath(repositoryPath)} with {providerOptions.Model}...");
    var result = await repositoryReviewer.ReviewAsync(repositoryPath, reviewOptions);
    PrintRepositoryResult(result);
    return result.Statistics.FilesReviewed > 0 || result.Statistics.FilesDiscovered == 0 ? 0 : 1;
}

static async Task<int> ReviewRepositoryWithRagAsync(string repositoryPath, string[] optionArguments)
{
    var reviewOptions = ParseRepositoryOptions(optionArguments);
    var providerOptions = OpenAiOptions.FromEnvironment();
    var embeddingOptions = OpenAiEmbeddingOptions.FromEnvironment(providerOptions);
    using var httpClient = CreateHttpClient();
    var reviewer = new OpenAiCodeReviewService(httpClient, providerOptions);
    var embeddingService = new OpenAiEmbeddingService(httpClient, embeddingOptions);
    var repositoryReviewer = new RagRepositoryReviewService(
        new RepositoryScanner(),
        new SourceFileLoader(),
        new SourceCodeChunker(),
        embeddingService,
        new InMemoryVectorIndex(),
        reviewer);

    Console.WriteLine($"Indexing and reviewing repository {Path.GetFullPath(repositoryPath)} with {providerOptions.Model} and {embeddingOptions.Model}...");
    var result = await repositoryReviewer.ReviewAsync(repositoryPath, reviewOptions);
    PrintRepositoryResult(result);
    return result.Statistics.FilesReviewed > 0 || result.Statistics.FilesDiscovered == 0 ? 0 : 1;
}

static async Task<int> ReviewRepositoryWithAgentAsync(string repositoryPath)
{
    var providerOptions = OpenAiOptions.FromEnvironment();
    using var httpClient = CreateHttpClient();
    var agent = new OpenAiAgentReviewService(httpClient, providerOptions);
    var options = new AgentReviewOptions();

    Console.WriteLine($"Agentically reviewing repository {Path.GetFullPath(repositoryPath)} with {providerOptions.Model}...");
    var result = await agent.ReviewRepositoryAsync(repositoryPath, options);
    PrintFindings(result.Findings);
    Console.WriteLine();
    Console.WriteLine($"Investigation steps ({result.Investigation.Count}/{options.MaximumToolCalls}):");
    foreach (var step in result.Investigation)
    {
        Console.WriteLine($"  {step.Number}. {step.Tool} {step.Arguments} -> {step.OutputCharacters:N0} characters");
    }

    PrintApiUsage(result.Model, result.Usage);
    return 0;
}

static async Task<int> ReviewPullRequestAsync(
    string owner,
    string repository,
    string pullNumberText,
    string[] approvalArguments)
{
    if (!int.TryParse(pullNumberText, NumberStyles.None, CultureInfo.InvariantCulture, out var pullNumber) || pullNumber <= 0)
    {
        throw new ArgumentException("The pull-request number must be a positive integer.");
    }

    if (approvalArguments.Length > 1 || approvalArguments.Any(argument => argument != "--approve"))
    {
        throw new ArgumentException("The only supported pull-request option is --approve.");
    }

    var approved = approvalArguments.Contains("--approve", StringComparer.Ordinal);
    var providerOptions = OpenAiOptions.FromEnvironment();
    using var httpClient = CreateHttpClient();
    var githubClient = new GitHubPullRequestClient(httpClient, GitHubOptions.FromEnvironment());
    var service = new PullRequestReviewService(
        githubClient,
        new OpenAiCodeReviewService(httpClient, providerOptions),
        new LanguageDetector());
    var reference = new PullRequestReference(owner, repository, pullNumber);

    Console.WriteLine($"Reviewing GitHub pull request {owner}/{repository}#{pullNumber}...");
    var (pullRequest, review) = await service.ReviewAsync(reference);
    PrintFindings(review.Findings);
    Console.WriteLine();
    Console.WriteLine($"Draft inline comments: {review.DraftComments.Count}");
    foreach (var comment in review.DraftComments)
    {
        Console.WriteLine($"  {comment.Path}:{comment.Line} — {comment.Body.Replace(Environment.NewLine, " ", StringComparison.Ordinal)}");
    }

    if (approved)
    {
        await githubClient.PostReviewAsync(pullRequest, review.DraftComments, humanApproved: true);
        Console.WriteLine("Published the approved GitHub review.");
    }
    else
    {
        Console.WriteLine("Dry run only. Review the drafts, then rerun with --approve to publish them.");
    }

    PrintApiUsage(review.Model, review.Usage);
    return 0;
}

static async Task<int> ReviewWithSpecialistsAsync(string sourcePath)
{
    var sourceFile = await new SourceFileLoader().LoadAsync(sourcePath);
    var providerOptions = OpenAiOptions.FromEnvironment();
    using var httpClient = CreateHttpClient();
    var coordinator = new MultiAgentReviewCoordinator(new OpenAiCodeReviewService(httpClient, providerOptions));

    Console.WriteLine($"Running {SpecialistProfile.All.Count} specialist reviewers over {sourceFile.Name}...");
    var result = await coordinator.ReviewAsync(sourceFile);
    Console.WriteLine();
    Console.WriteLine($"Unified findings ({result.Findings.Count}) from {result.SpecialistCalls} specialist calls:");
    foreach (var attributed in result.Findings)
    {
        var finding = attributed.Finding;
        Console.WriteLine();
        Console.WriteLine($"[{finding.Severity}] {finding.Category} — {finding.File} {finding.Location}");
        Console.WriteLine($"  Specialists: {string.Join(", ", attributed.Specialists)}");
        Console.WriteLine($"  {finding.Explanation}");
        Console.WriteLine($"  Suggested improvement: {finding.SuggestedImprovement}");
    }

    PrintApiUsage(result.Model, result.Usage);
    return 0;
}

static async Task<int> ReviewRemoteRepositoryAsync(string remoteUrl, string[] optionArguments)
{
    Console.WriteLine($"Creating an isolated shallow checkout of {remoteUrl}...");
    using var checkout = await RemoteGitRepository.CloneAsync(remoteUrl);
    return await ReviewRepositoryAsync(checkout.Path, optionArguments);
}

static void PrintRepositoryResult(RepositoryReviewResult result)
{
    PrintFindings(result.Findings);

    Console.WriteLine();
    Console.WriteLine("Repository statistics:");
    Console.WriteLine($"  Files discovered: {result.Statistics.FilesDiscovered:N0}");
    Console.WriteLine($"  Files reviewed:   {result.Statistics.FilesReviewed:N0}");
    Console.WriteLine($"  Files skipped:    {result.Statistics.FilesSkipped:N0}");
    Console.WriteLine($"  Files failed:     {result.Statistics.FilesFailed:N0}");
    Console.WriteLine($"  Characters sent:  {result.Statistics.CharactersReviewed:N0}");
    if (result.Statistics.ChunksIndexed > 0)
    {
        Console.WriteLine($"  Chunks indexed:   {result.Statistics.ChunksIndexed:N0}");
        Console.WriteLine($"  Context retrieved:{result.Statistics.ContextChunksRetrieved,10:N0}");
        Console.WriteLine($"  Embedding tokens: {FormatTokenCount(result.EmbeddingTokens)}");
    }

    if (result.Failures.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Per-file failures:");
        foreach (var failure in result.Failures)
        {
            Console.WriteLine($"  {failure.File}: {failure.Error}");
        }
    }

    PrintApiUsage(result.Model, result.Usage);
}

static RepositoryReviewOptions ParseRepositoryOptions(string[] arguments)
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
            case "--max-files" when int.TryParse(arguments[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsedFiles):
                maximumFiles = parsedFiles;
                break;
            case "--max-chars" when long.TryParse(arguments[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsedCharacters):
                maximumCharacters = parsedCharacters;
                break;
            case "--max-files":
            case "--max-chars":
                throw new ArgumentException($"The value for {arguments[index]} must be a positive integer.");
            default:
                throw new ArgumentException($"Unknown repository option: {arguments[index]}");
        }
    }

    var options = new RepositoryReviewOptions(maximumFiles, maximumCharacters);
    options.Validate();
    return options;
}

static HttpClient CreateHttpClient() => new() { Timeout = TimeSpan.FromMinutes(2) };

static string? PromptForPath()
{
    Console.Write("Source file path: ");
    return Console.ReadLine()?.Trim().Trim('"');
}

static int ShowMissingPathError()
{
    Console.Error.WriteLine("Error: a source file path is required.");
    return 2;
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
    foreach (var fileGroup in findings.GroupBy(finding => finding.File).OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine();
        Console.WriteLine(fileGroup.Key);
        foreach (var categoryGroup in fileGroup.GroupBy(finding => finding.Category).OrderBy(group => group.Key))
        {
            Console.WriteLine($"  {categoryGroup.Key}:");
            foreach (var finding in categoryGroup)
            {
                Console.WriteLine($"    {index++}. [{finding.Severity}] {finding.Explanation}");
                if (!string.IsNullOrWhiteSpace(finding.Location))
                {
                    Console.WriteLine($"       Location: {finding.Location}");
                }

                Console.WriteLine($"       Suggested improvement: {finding.SuggestedImprovement}");
            }
        }
    }
}

static void PrintApiUsage(string model, ApiUsage? usage)
{
    Console.WriteLine();
    Console.WriteLine($"Model: {model}");
    if (usage is null)
    {
        Console.WriteLine("Token usage: unavailable");
        return;
    }

    Console.WriteLine($"Token usage: input {FormatTokenCount(usage.InputTokens)}, output {FormatTokenCount(usage.OutputTokens)}, total {FormatTokenCount(usage.TotalTokens)}");
}

static string FormatTokenCount(int? count) => count?.ToString("N0", CultureInfo.InvariantCulture) ?? "unavailable";

static void PrintHelp()
{
    Console.WriteLine("Review one file:");
    Console.WriteLine("  dotnet run --project src/AiCodeReviewer.Cli -- review-file <path>");
    Console.WriteLine();
    Console.WriteLine("Review a local repository:");
    Console.WriteLine("  dotnet run --project src/AiCodeReviewer.Cli -- review-repo <directory> [--max-files N] [--max-chars N]");
    Console.WriteLine();
    Console.WriteLine("Review a local repository with semantic retrieval:");
    Console.WriteLine("  dotnet run --project src/AiCodeReviewer.Cli -- review-rag <directory> [--max-files N] [--max-chars N]");
    Console.WriteLine();
    Console.WriteLine("Let an AI agent investigate a local repository with read-only tools:");
    Console.WriteLine("  dotnet run --project src/AiCodeReviewer.Cli -- review-agent <directory>");
    Console.WriteLine();
    Console.WriteLine("Review a GitHub pull request (dry run unless explicitly approved):");
    Console.WriteLine("  dotnet run --project src/AiCodeReviewer.Cli -- review-pr <owner> <repo> <number> [--approve]");
    Console.WriteLine();
    Console.WriteLine("Review a source file with five coordinated specialists:");
    Console.WriteLine("  dotnet run --project src/AiCodeReviewer.Cli -- review-specialists <path>");
    Console.WriteLine();
    Console.WriteLine("Review a remote Git repository through an isolated shallow checkout:");
    Console.WriteLine("  dotnet run --project src/AiCodeReviewer.Cli -- review-remote <https-git-url> [--max-files N] [--max-chars N]");
}
