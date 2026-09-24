using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AiCodeReviewer.Core.Exceptions;

namespace AiCodeReviewer.GitHub;

public sealed class GitHubPullRequestClient : IGitHubPullRequestClient
{
    private readonly HttpClient _httpClient;
    private readonly GitHubOptions _options;

    public GitHubPullRequestClient(HttpClient httpClient, GitHubOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<PullRequestData> GetAsync(
        PullRequestReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        reference.Validate();
        var path = $"repos/{Uri.EscapeDataString(reference.Owner)}/{Uri.EscapeDataString(reference.Repository)}/pulls/{reference.Number}";

        using var metadataRequest = CreateRequest(HttpMethod.Get, path, "application/vnd.github+json");
        using var metadataResponse = await _httpClient.SendAsync(metadataRequest, cancellationToken);
        var metadataText = await metadataResponse.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(metadataResponse, metadataText);
        string headSha;
        try
        {
            using var document = JsonDocument.Parse(metadataText);
            headSha = document.RootElement.GetProperty("head").GetProperty("sha").GetString()
                ?? throw new JsonException("Missing head SHA.");
        }
        catch (JsonException exception)
        {
            throw new RepositoryException("GitHub returned malformed pull-request metadata.", exception);
        }

        using var diffRequest = CreateRequest(HttpMethod.Get, path, "application/vnd.github.v3.diff");
        using var diffResponse = await _httpClient.SendAsync(diffRequest, cancellationToken);
        var diff = await diffResponse.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(diffResponse, diff);
        return new PullRequestData(reference, headSha, diff);
    }

    public async Task PostReviewAsync(
        PullRequestData pullRequest,
        IReadOnlyList<DraftReviewComment> comments,
        bool humanApproved,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);
        ArgumentNullException.ThrowIfNull(comments);
        if (!humanApproved)
        {
            throw new InvalidOperationException("Human approval is required before publishing GitHub comments.");
        }

        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            throw new InvalidOperationException("GITHUB_TOKEN is required to publish a review.");
        }

        var reference = pullRequest.Reference;
        var path = $"repos/{Uri.EscapeDataString(reference.Owner)}/{Uri.EscapeDataString(reference.Repository)}/pulls/{reference.Number}/reviews";
        using var request = CreateRequest(HttpMethod.Post, path, "application/vnd.github+json");
        request.Content = JsonContent.Create(new
        {
            commit_id = pullRequest.HeadSha,
            body = "AI-assisted review. Every finding should be verified by a human.",
            @event = "COMMENT",
            comments = comments.Select(comment => new { path = comment.Path, line = comment.Line, side = "RIGHT", body = comment.Body })
        });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, responseText);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, string accept)
    {
        var request = new HttpRequestMessage(method, new Uri(_options.BaseUri, path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        request.Headers.UserAgent.ParseAdd("AiCodeReviewer/1.0");
        if (!string.IsNullOrWhiteSpace(_options.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);
        }

        return request;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string responseText)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new RepositoryException($"GitHub API request failed with HTTP {(int)response.StatusCode}: {responseText[..Math.Min(500, responseText.Length)]}");
        }
    }
}
