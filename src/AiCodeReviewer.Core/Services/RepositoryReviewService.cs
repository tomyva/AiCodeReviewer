using AiCodeReviewer.Core.Abstractions;
using AiCodeReviewer.Core.Exceptions;
using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Core.Services;

public sealed class RepositoryReviewService : IRepositoryReviewService
{
    private readonly IRepositoryScanner _scanner;
    private readonly ISourceFileLoader _fileLoader;
    private readonly ICodeReviewService _codeReviewService;

    public RepositoryReviewService(
        IRepositoryScanner scanner,
        ISourceFileLoader fileLoader,
        ICodeReviewService codeReviewService)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _fileLoader = fileLoader ?? throw new ArgumentNullException(nameof(fileLoader));
        _codeReviewService = codeReviewService ?? throw new ArgumentNullException(nameof(codeReviewService));
    }

    public async Task<RepositoryReviewResult> ReviewAsync(
        string repositoryPath,
        RepositoryReviewOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var scan = _scanner.Scan(repositoryPath);
        var selectedFiles = SelectFiles(scan.Files, options);
        var findings = new List<ReviewFinding>();
        var failures = new List<RepositoryFileFailure>();
        var usages = new List<ApiUsage>();
        var reviewed = 0;
        long charactersReviewed = 0;
        string? model = null;

        foreach (var repositoryFile in selectedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var loaded = await _fileLoader.LoadAsync(repositoryFile.FullPath, cancellationToken);
                var sourceFile = loaded with { Name = repositoryFile.RelativePath };
                var result = await _codeReviewService.ReviewAsync(sourceFile, cancellationToken);
                findings.AddRange(result.Findings.Select(finding => finding with { File = repositoryFile.RelativePath }));
                if (result.Usage is not null)
                {
                    usages.Add(result.Usage);
                }

                model ??= result.Model;
                reviewed++;
                charactersReviewed += sourceFile.Content.Length;
            }
            catch (Exception exception) when (exception is SourceFileException or CodeReviewServiceException)
            {
                failures.Add(new RepositoryFileFailure(repositoryFile.RelativePath, exception.Message));
            }
        }

        var budgetSkipped = scan.Files.Count - selectedFiles.Count;
        var statistics = new RepositoryReviewStatistics(
            scan.FilesDiscovered,
            reviewed,
            scan.FilesSkipped + budgetSkipped,
            failures.Count,
            charactersReviewed,
            ChunksIndexed: 0,
            ContextChunksRetrieved: 0);

        return new RepositoryReviewResult(
            findings,
            statistics,
            failures,
            AggregateUsage(usages),
            EmbeddingTokens: null,
            model ?? "unavailable");
    }

    private static List<RepositoryFile> SelectFiles(
        IReadOnlyList<RepositoryFile> files,
        RepositoryReviewOptions options)
    {
        var selected = new List<RepositoryFile>();
        long characters = 0;

        foreach (var file in files)
        {
            if (selected.Count >= options.MaximumFiles)
            {
                break;
            }

            if (file.SizeBytes > SourceFileLoader.DefaultMaximumFileSizeBytes ||
                characters + file.SizeBytes > options.MaximumTotalCharacters)
            {
                continue;
            }

            selected.Add(file);
            characters += file.SizeBytes;
        }

        return selected;
    }

    private static ApiUsage? AggregateUsage(List<ApiUsage> usages)
    {
        if (usages.Count == 0)
        {
            return null;
        }

        return new ApiUsage(
            SumAvailable(usages.Select(usage => usage.InputTokens)),
            SumAvailable(usages.Select(usage => usage.OutputTokens)),
            SumAvailable(usages.Select(usage => usage.TotalTokens)));
    }

    private static int? SumAvailable(IEnumerable<int?> values)
    {
        var materialized = values.ToArray();
        return materialized.All(value => value.HasValue) ? materialized.Sum(value => value!.Value) : null;
    }
}
