namespace AiCodeReviewer.Application;

public static class ConfigurationInspector
{
    public static ConfigurationStatus Inspect()
    {
        var hasKey = HasValue("OPENAI_API_KEY");
        var hasModel = HasValue("OPENAI_MODEL");
        var hasGitHubToken = HasValue("GITHUB_TOKEN");
        var messages = new List<string>();

        if (!hasKey)
        {
            messages.Add("OPENAI_API_KEY is not configured.");
        }

        if (!hasModel)
        {
            messages.Add("OPENAI_MODEL is not configured.");
        }

        if (!hasGitHubToken)
        {
            messages.Add("GITHUB_TOKEN is optional for reviews and required only when publishing pull-request comments.");
        }

        return new ConfigurationStatus(
            hasKey,
            hasModel,
            hasGitHubToken,
            Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "not configured",
            Environment.GetEnvironmentVariable("OPENAI_EMBEDDING_MODEL") ?? "text-embedding-3-small",
            messages);
    }

    private static bool HasValue(string name) =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name));
}
