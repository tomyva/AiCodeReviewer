using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AiCodeReviewer.Core.Abstractions;
using AiCodeReviewer.Core.Exceptions;
using AiCodeReviewer.Core.Models;

namespace AiCodeReviewer.OpenAI;

public sealed class OpenAiEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly OpenAiEmbeddingOptions _options;

    public OpenAiEmbeddingService(HttpClient httpClient, OpenAiEmbeddingOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<EmbeddingBatch> EmbedAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0 || inputs.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one non-empty embedding input is required.", nameof(inputs));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.BaseUri, "embeddings"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = JsonContent.Create(new
        {
            model = _options.Model,
            input = inputs,
            encoding_format = "float"
        });

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CodeReviewServiceException("The embedding request timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new CodeReviewServiceException("The embedding provider could not be reached.", exception);
        }

        using (response)
        {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new CodeReviewServiceException($"The embedding request failed with HTTP {(int)response.StatusCode}.");
            }

            return ParseResponse(responseText, inputs.Count);
        }
    }

    private static EmbeddingBatch ParseResponse(string responseText, int expectedCount)
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            var vectors = root.GetProperty("data")
                .EnumerateArray()
                .OrderBy(item => item.GetProperty("index").GetInt32())
                .Select(item => item.GetProperty("embedding").EnumerateArray().Select(value => value.GetSingle()).ToArray())
                .ToArray();
            if (vectors.Length != expectedCount || vectors.Any(vector => vector.Length == 0))
            {
                throw new CodeReviewServiceException("The embedding provider returned an unexpected number of vectors.");
            }

            int? tokens = null;
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var promptTokens) && promptTokens.TryGetInt32(out var promptValue))
                {
                    tokens = promptValue;
                }
                else if (usage.TryGetProperty("total_tokens", out var totalTokens) && totalTokens.TryGetInt32(out var totalValue))
                {
                    tokens = totalValue;
                }
            }

            return new EmbeddingBatch(vectors, tokens);
        }
        catch (CodeReviewServiceException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new CodeReviewServiceException("The embedding provider returned malformed output.", exception);
        }
    }
}
