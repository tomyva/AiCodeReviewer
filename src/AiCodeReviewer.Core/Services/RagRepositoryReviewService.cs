using AiCodeReviewer.Core.Abstractions;
using AiCodeReviewer.Core.Exceptions;
using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.Core.Services;

public sealed class RagRepositoryReviewService : IRepositoryReviewService
{
    private const int RetrievedChunksPerFile = 3;
    private readonly IRepositoryScanner _scanner;
    private readonly ISourceFileLoader _fileLoader;
    private readonly ICodeChunker _chunker;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorIndex _vectorIndex;
    private readonly IRetrievalAwareCodeReviewService _reviewer;

    public RagRepositoryReviewService(
        IRepositoryScanner scanner,
        ISourceFileLoader fileLoader,
        ICodeChunker chunker,
        IEmbeddingService embeddingService,
        IVectorIndex vectorIndex,
        IRetrievalAwareCodeReviewService reviewer)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _fileLoader = fileLoader ?? throw new ArgumentNullException(nameof(fileLoader));
        _chunker = chunker ?? throw new ArgumentNullException(nameof(chunker));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _vectorIndex = vectorIndex ?? throw new ArgumentNullException(nameof(vectorIndex));
        _reviewer = reviewer ?? throw new ArgumentNullException(nameof(reviewer));
    }

    public async Task<RepositoryReviewResult> ReviewAsync(
        string repositoryPath,
        RepositoryReviewOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var scan = _scanner.Scan(repositoryPath);
        var selected = SelectFiles(scan.Files, options);
        var sources = new List<SourceFile>();
        var failures = new List<RepositoryFileFailure>();

        foreach (var file in selected)
        {
            try
            {
                var loaded = await _fileLoader.LoadAsync(file.FullPath, cancellationToken);
                sources.Add(loaded with { Name = file.RelativePath });
            }
            catch (SourceFileException exception)
            {
                failures.Add(new RepositoryFileFailure(file.RelativePath, exception.Message));
            }
        }

        var chunks = sources.SelectMany(source => _chunker.Chunk(source)).ToArray();
        var embeddingTokens = 0;
        if (chunks.Length > 0)
        {
            var embeddings = await _embeddingService.EmbedAsync(
                chunks.Select(FormatEmbeddingInput).ToArray(),
                cancellationToken);
            embeddingTokens += embeddings.InputTokens ?? 0;
            _vectorIndex.Replace(chunks.Zip(embeddings.Vectors, (chunk, vector) => new EmbeddedCodeChunk(chunk, vector)).ToArray());
        }

        var findings = new List<ReviewFinding>();
        var usages = new List<ApiUsage>();
        var reviewed = 0;
        var retrievedCount = 0;
        long charactersReviewed = 0;
        string? model = null;

        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var query = await _embeddingService.EmbedAsync([CreateQuery(source)], cancellationToken);
                embeddingTokens += query.InputTokens ?? 0;
                var related = _vectorIndex.Search(
                        query.Vectors[0],
                        RetrievedChunksPerFile,
                        chunk => !string.Equals(chunk.File, source.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(result => result.Chunk)
                    .ToArray();
                retrievedCount += related.Length;

                var result = await _reviewer.ReviewWithContextAsync(source, related, cancellationToken);
                findings.AddRange(result.Findings.Select(finding => finding with { File = source.Name }));
                if (result.Usage is not null)
                {
                    usages.Add(result.Usage);
                }

                model ??= result.Model;
                reviewed++;
                charactersReviewed += source.Content.Length;
            }
            catch (CodeReviewServiceException exception)
            {
                failures.Add(new RepositoryFileFailure(source.Name, exception.Message));
            }
        }

        var statistics = new RepositoryReviewStatistics(
            scan.FilesDiscovered,
            reviewed,
            scan.FilesSkipped + scan.Files.Count - selected.Count,
            failures.Count,
            charactersReviewed,
            chunks.Length,
            retrievedCount);

        return new RepositoryReviewResult(
            findings,
            statistics,
            failures,
            AggregateUsage(usages),
            embeddingTokens,
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

    private static string FormatEmbeddingInput(CodeChunk chunk) =>
        $"File: {chunk.File}\nLanguage: {chunk.Language.DisplayName}\nLines: {chunk.StartLine}-{chunk.EndLine}\n{chunk.Content}";

    private static string CreateQuery(SourceFile source)
    {
        const int maximumQueryCharacters = 6_000;
        var content = source.Content.Length <= maximumQueryCharacters
            ? source.Content
            : source.Content[..maximumQueryCharacters];
        return $"Find code in the repository related to {source.Name}, its symbols, dependencies, callers, contracts, and behavior.\n{content}";
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
