# AI Code Reviewer — Version 1

A .NET command-line application that sends one C# source file to an AI model and prints structured code-review findings. This repository intentionally implements **Version 1 only**: there is no repository scanning, multi-language support, embeddings, RAG, tool calling, agents, or GitHub integration.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or newer
- An OpenAI API key and access to a model that supports Structured Outputs

## Build and test

```powershell
dotnet restore
dotnet build --no-restore
dotnet test --no-build
```

The tests cover deterministic, non-AI behavior: file validation/loading and prompt construction. They do not use credentials or make API calls.

## Configure the AI provider

Credentials and model selection are read from environment variables and are never stored in source code.

```powershell
$env:OPENAI_API_KEY = "your-api-key"
$env:OPENAI_MODEL = "gpt-5.4-mini"
```

Optional: set `OPENAI_BASE_URL` to change the API base URL. It defaults to `https://api.openai.com/v1/`.

Do not commit API keys. The `.gitignore` excludes `.env`, but this application deliberately does not load `.env` files automatically.

## Run a review

Pass a single UTF-8 C# file (maximum 512 KiB):

```powershell
dotnet run --project src/AiCodeReviewer.Cli -- "C:\path\to\Example.cs"
```

Run without a path to receive an interactive path prompt:

```powershell
dotnet run --project src/AiCodeReviewer.Cli
```

The command prints each finding's severity, category, explanation, location (when available), and suggested improvement. It also prints input, output, and total token counts when the provider returns them.

## Architecture

The solution separates policy, provider transport, and presentation:

- `AiCodeReviewer.Core` contains provider-neutral models, `ICodeReviewService`, file validation, and prompt construction.
- `AiCodeReviewer.OpenAI` implements `ICodeReviewService` using the OpenAI Responses API. It requests strict JSON Schema output and translates provider/network failures into an application exception.
- `AiCodeReviewer.Cli` handles configuration, user input, and console rendering.
- `AiCodeReviewer.Tests` tests only deterministic core behavior.

The CLI composes the OpenAI implementation at startup. A later provider can implement `ICodeReviewService` without changing the file loader or review result model.

## Structured output

The provider is required to return a `findings` array. Each finding contains:

- `severity`: info, low, medium, high, or critical
- `category`: `bug`, `maintainability`, `performance`, `security`, `codeQuality`, `refactoring`, `errorHandling`, or `testing`
- `explanation`
- `location`: a line range or code identifier when available, otherwise null
- `suggestedImprovement`

## AI concepts demonstrated

Version 1 demonstrates the basic AI application loop: create a focused prompt, call an LLM API, constrain the response with a strict schema, deserialize the result into typed C# models, expose token usage, and isolate provider-specific behavior behind an interface.

## Known limitations

- Reviews exactly one C# file at a time and cannot reason about other files or repository context.
- Results are probabilistic and require human verification.
- Token counts are shown, but monetary cost is not calculated because pricing varies by model and can change.
- The CLI does not retry rate limits or transient failures.
- The maximum file-size check is a safety guard, not a model-specific token-budget calculation.

Future versions can add broader capabilities described by the master specification, but they are intentionally outside this implementation.
