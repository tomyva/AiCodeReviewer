namespace AiCodeReviewer.GitHub;

public sealed record GitHubOptions(string? Token, Uri BaseUri)
{
    public static GitHubOptions FromEnvironment() => new(
        Environment.GetEnvironmentVariable("GITHUB_TOKEN"),
        new Uri(Environment.GetEnvironmentVariable("GITHUB_API_URL") ?? "https://api.github.com/"));
}
