namespace AiCodeReviewer.OpenAI;

public sealed record OpenAiOptions(string ApiKey, string Model, Uri BaseUri)
{
    public static OpenAiOptions FromEnvironment()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var model = Environment.GetEnvironmentVariable("OPENAI_MODEL");
        var baseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "https://api.openai.com/v1/";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OPENAI_API_KEY is not configured.");
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException("OPENAI_MODEL is not configured.");
        }

        if (!Uri.TryCreate(baseUrl.EndsWith('/') ? baseUrl : baseUrl + '/', UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException("OPENAI_BASE_URL is not a valid absolute URL.");
        }

        return new OpenAiOptions(apiKey, model, baseUri);
    }
}
