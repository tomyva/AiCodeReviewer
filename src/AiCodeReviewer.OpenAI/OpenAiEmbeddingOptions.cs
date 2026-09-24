namespace AiCodeReviewer.OpenAI;

public sealed record OpenAiEmbeddingOptions(string ApiKey, string Model, Uri BaseUri)
{
    public static OpenAiEmbeddingOptions FromEnvironment(OpenAiOptions providerOptions)
    {
        ArgumentNullException.ThrowIfNull(providerOptions);
        var model = Environment.GetEnvironmentVariable("OPENAI_EMBEDDING_MODEL") ?? "text-embedding-3-small";
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException("OPENAI_EMBEDDING_MODEL cannot be empty.");
        }

        return new OpenAiEmbeddingOptions(providerOptions.ApiKey, model, providerOptions.BaseUri);
    }
}
