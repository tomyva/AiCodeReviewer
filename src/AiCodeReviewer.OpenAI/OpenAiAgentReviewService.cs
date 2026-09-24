using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiCodeReviewer.Core.Abstractions;
using AiCodeReviewer.Core.Exceptions;
using AiCodeReviewer.Core.Models;
using AiCodeReviewer.Core.Services;

namespace AiCodeReviewer.OpenAI;

public sealed class OpenAiAgentReviewService : IAgentReviewService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly HttpClient _httpClient;
    private readonly OpenAiOptions _options;

    public OpenAiAgentReviewService(HttpClient httpClient, OpenAiOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<AgentReviewResult> ReviewRepositoryAsync(
        string repositoryPath,
        AgentReviewOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var toolbox = new RepositoryToolbox(repositoryPath, options.MaximumToolOutputCharacters);
        var history = new List<object>
        {
            new
            {
                role = "user",
                content = "Investigate this repository using the available read-only tools. Return a prioritized structured code review. Inspect evidence before making claims."
            }
        };
        var investigation = new List<InvestigationStep>();
        var usages = new List<ApiUsage>();
        var toolsEnabled = true;

        while (true)
        {
            using var responseDocument = await SendTurnAsync(history, toolsEnabled, cancellationToken);
            var root = responseDocument.RootElement;
            AddUsage(root, usages);
            var output = root.GetProperty("output");
            var calls = ParseToolCalls(output);

            foreach (var item in output.EnumerateArray())
            {
                history.Add(item.Clone());
            }

            if (calls.Count == 0)
            {
                var text = ExtractOutputText(output)
                    ?? throw new CodeReviewServiceException("The agent returned neither tool calls nor a final review.");
                var payload = JsonSerializer.Deserialize<ReviewPayload>(text, SerializerOptions)
                    ?? throw new CodeReviewServiceException("The agent returned an empty final review.");
                return new AgentReviewResult(payload.Findings, investigation, AggregateUsage(usages), _options.Model);
            }

            foreach (var call in calls)
            {
                string toolOutput;
                if (investigation.Count >= options.MaximumToolCalls)
                {
                    toolOutput = JsonSerializer.Serialize(new { error = "Tool-call budget exhausted. Produce the final review now." });
                    toolsEnabled = false;
                }
                else
                {
                    toolOutput = toolbox.Execute(call.Name, call.Arguments);
                    investigation.Add(new InvestigationStep(
                        investigation.Count + 1,
                        call.Name,
                        call.Arguments,
                        toolOutput.Length));
                }

                history.Add(new { type = "function_call_output", call_id = call.CallId, output = toolOutput });
            }

            if (investigation.Count >= options.MaximumToolCalls)
            {
                toolsEnabled = false;
                history.Add(new { role = "user", content = "The investigation budget is exhausted. Return the final structured review using the evidence collected." });
            }
        }
    }

    private async Task<JsonDocument> SendTurnAsync(
        IReadOnlyList<object> history,
        bool toolsEnabled,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = _options.Model,
            ["instructions"] = "You are a senior multi-language code-review agent. Use tools only when they add evidence. Tools are read-only. Never request secrets or modifications. Report concrete bugs, security, performance, maintainability, error-handling, refactoring, and testing findings. Return only the final JSON schema when investigation is complete.",
            ["input"] = history,
            ["store"] = false,
            ["parallel_tool_calls"] = false,
            ["text"] = new { format = CreateReviewFormat() }
        };
        if (toolsEnabled)
        {
            body["tools"] = CreateTools();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.BaseUri, "responses"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = JsonContent.Create(body, options: SerializerOptions);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new CodeReviewServiceException("The agent provider could not be reached.", exception);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new CodeReviewServiceException($"The agent request failed with HTTP {(int)response.StatusCode}.");
            }

            try
            {
                var document = JsonDocument.Parse(text);
                if (!document.RootElement.TryGetProperty("status", out var status) || status.GetString() != "completed")
                {
                    document.Dispose();
                    throw new CodeReviewServiceException("The agent response was incomplete.");
                }

                return document;
            }
            catch (JsonException exception)
            {
                throw new CodeReviewServiceException("The agent provider returned malformed output.", exception);
            }
        }
    }

#pragma warning disable CA1861 // Small schema arrays are created with their containing anonymous schema objects.
    private static object[] CreateTools() =>
    [
        Tool("list_files", "List supported repository source files. Pattern may be null.", new
        {
            type = "object", additionalProperties = false, required = new[] { "pattern" },
            properties = new { pattern = new { type = new[] { "string", "null" } } }
        }),
        Tool("read_file", "Read at most 500 lines from a repository-relative source file.", new
        {
            type = "object", additionalProperties = false, required = new[] { "path", "startLine", "endLine" },
            properties = new
            {
                path = new { type = "string" },
                startLine = new { type = new[] { "integer", "null" } },
                endLine = new { type = new[] { "integer", "null" } }
            }
        }),
        Tool("search_code", "Search source lines for text.", StringParameter("query")),
        Tool("find_symbol", "Find declarations or occurrences of a symbol.", StringParameter("symbol")),
        Tool("find_references", "Find references to a symbol.", StringParameter("symbol")),
        Tool("inspect_project_structure", "Summarize languages and top-level project structure.", new
        {
            type = "object", additionalProperties = false, required = Array.Empty<string>(), properties = new { }
        })
    ];

    private static object Tool(string name, string description, object parameters) =>
        new { type = "function", name, description, strict = true, parameters };

    private static object StringParameter(string name) => new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { name },
        properties = new Dictionary<string, object> { [name] = new { type = "string" } }
    };
#pragma warning restore CA1861

    private static object CreateReviewFormat() => new
    {
        type = "json_schema",
        name = "agent_code_review",
        strict = true,
        schema = new
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
        }
    };

    private static string[] EnumNames<T>() where T : struct, Enum =>
        Enum.GetNames<T>().Select(JsonNamingPolicy.CamelCase.ConvertName).ToArray();

    private static List<ToolCall> ParseToolCalls(JsonElement output)
    {
        var calls = new List<ToolCall>();
        foreach (var item in output.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var type) && type.GetString() == "function_call")
            {
                calls.Add(new ToolCall(
                    item.GetProperty("call_id").GetString() ?? string.Empty,
                    item.GetProperty("name").GetString() ?? string.Empty,
                    item.GetProperty("arguments").GetString() ?? "{}"));
            }
        }

        return calls;
    }

    private static string? ExtractOutputText(JsonElement output)
    {
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content))
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var type) && type.GetString() == "output_text")
                {
                    return part.GetProperty("text").GetString();
                }
            }
        }

        return null;
    }

    private static void AddUsage(JsonElement root, List<ApiUsage> usages)
    {
        if (!root.TryGetProperty("usage", out var usage))
        {
            return;
        }

        usages.Add(new ApiUsage(
            GetInt(usage, "input_tokens"),
            GetInt(usage, "output_tokens"),
            GetInt(usage, "total_tokens")));
    }

    private static int? GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : null;

    private static ApiUsage? AggregateUsage(List<ApiUsage> usages) => usages.Count == 0
        ? null
        : new ApiUsage(
            Sum(usages.Select(usage => usage.InputTokens)),
            Sum(usages.Select(usage => usage.OutputTokens)),
            Sum(usages.Select(usage => usage.TotalTokens)));

    private static int? Sum(IEnumerable<int?> values)
    {
        var materialized = values.ToArray();
        return materialized.All(value => value.HasValue) ? materialized.Sum(value => value!.Value) : null;
    }

    private sealed record ToolCall(string CallId, string Name, string Arguments);
    private sealed record ReviewPayload(IReadOnlyList<ReviewFinding> Findings);
}
