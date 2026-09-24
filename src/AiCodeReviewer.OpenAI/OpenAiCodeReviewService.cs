using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiCodeReviewer.Core.Abstractions;
using AiCodeReviewer.Core.Exceptions;
using AiCodeReviewer.Core.Models;
using AiCodeReviewer.Core.Services;

namespace AiCodeReviewer.OpenAI;

public sealed class OpenAiCodeReviewService : IRetrievalAwareCodeReviewService, ISpecialistCodeReviewService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;
    public OpenAiCodeReviewService(HttpClient httpClient, OpenAiOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<CodeReviewResult> ReviewAsync(SourceFile sourceFile, CancellationToken cancellationToken = default) =>
        ReviewCoreAsync(sourceFile, null, null, cancellationToken);

    public Task<CodeReviewResult> ReviewWithContextAsync(
        SourceFile sourceFile,
        IReadOnlyList<CodeChunk> relatedContext,
        CancellationToken cancellationToken = default) =>
        ReviewCoreAsync(sourceFile, relatedContext, null, cancellationToken);

    public Task<CodeReviewResult> ReviewAsSpecialistAsync(
        SourceFile sourceFile,
        SpecialistProfile specialist,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specialist);
        return ReviewCoreAsync(sourceFile, null, specialist.Guidance, cancellationToken);
    }

    private async Task<CodeReviewResult> ReviewCoreAsync(
        SourceFile sourceFile,
        IReadOnlyList<CodeChunk>? relatedContext,
        string? specialistGuidance,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceFile);
        var prompt = ReviewPromptFactory.Create(sourceFile, relatedContext, specialistGuidance);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.BaseUri, "responses"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = JsonContent.Create(CreateRequestBody(prompt), options: SerializerOptions);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CodeReviewServiceException("The AI provider request timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new CodeReviewServiceException("The AI provider could not be reached.", exception);
        }

        using (response)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateApiException(response.StatusCode, responseText);
            }

            return ParseResponse(responseText);
        }
    }

    private object CreateRequestBody(ReviewPrompt prompt) => new
    {
        model = _options.Model,
        instructions = prompt.Instructions,
        input = prompt.Input,
        store = false,
        text = new
        {
            format = new
            {
                type = "json_schema",
                name = "code_review",
                strict = true,
                schema = CreateReviewSchema()
            }
        }
    };

    private static object CreateReviewSchema() => new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "findings" },
        properties = new
        {
            findings = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "severity", "category", "file", "explanation", "location", "suggestedImprovement" },
                    properties = new
                    {
                        severity = new { type = "string", @enum = EnumNames<ReviewSeverity>() },
                        category = new { type = "string", @enum = EnumNames<ReviewCategory>() },
                        file = new { type = "string" },
                        explanation = new { type = "string" },
                        location = new { type = new[] { "string", "null" } },
                        suggestedImprovement = new { type = "string" }
                    }
                }
            }
        }
    };

    private static string[] EnumNames<T>() where T : struct, Enum =>
        Enum.GetNames<T>().Select(JsonNamingPolicy.CamelCase.ConvertName).ToArray();

    private CodeReviewResult ParseResponse(string responseText)
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            var status = root.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
            if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                throw new CodeReviewServiceException($"The AI provider returned an incomplete response (status: {status ?? "unknown"}).");
            }

            var outputText = ExtractOutputText(root)
                ?? throw new CodeReviewServiceException("The AI provider response did not contain structured review output.");
            var payload = JsonSerializer.Deserialize<ReviewPayload>(outputText, SerializerOptions)
                ?? throw new CodeReviewServiceException("The AI provider returned an empty review.");

            return new CodeReviewResult(payload.Findings, ParseUsage(root), _options.Model);
        }
        catch (CodeReviewServiceException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new CodeReviewServiceException("The AI provider returned malformed structured output.", exception);
        }
    }

    private static string? ExtractOutputText(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var type) && type.GetString() == "output_text" &&
                    part.TryGetProperty("text", out var text))
                {
                    return text.GetString();
                }
            }
        }

        return null;
    }

    private static ApiUsage? ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new ApiUsage(
            GetOptionalInt(usage, "input_tokens"),
            GetOptionalInt(usage, "output_tokens"),
            GetOptionalInt(usage, "total_tokens"));
    }

    private static int? GetOptionalInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value) ? value : null;

    private static CodeReviewServiceException CreateApiException(HttpStatusCode statusCode, string responseText)
    {
        var detail = TryReadErrorMessage(responseText);
        var message = statusCode switch
        {
            HttpStatusCode.Unauthorized => "The AI provider rejected the API key.",
            HttpStatusCode.TooManyRequests => "The AI provider rate limit or quota was exceeded.",
            HttpStatusCode.BadRequest => "The AI provider rejected the review request.",
            _ when (int)statusCode >= 500 => "The AI provider is temporarily unavailable.",
            _ => $"The AI provider request failed with HTTP {(int)statusCode}."
        };

        return new CodeReviewServiceException(string.IsNullOrWhiteSpace(detail) ? message : $"{message} {detail}");
    }

    private static string? TryReadErrorMessage(string responseText)
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);
            return document.RootElement.TryGetProperty("error", out var error) &&
                   error.TryGetProperty("message", out var message)
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ReviewPayload(IReadOnlyList<ReviewFinding> Findings);
}
