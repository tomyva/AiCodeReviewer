# AI Code Reviewer — Version 7

A .NET command-line application that reviews individual files, repositories, and GitHub pull requests using direct, RAG, agentic, or specialist workflows. Version 7 adds five focused reviewer roles and a coordinator that deduplicates their findings while preserving attribution.

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
$env:OPENAI_EMBEDDING_MODEL = "text-embedding-3-small" # optional default
$env:GITHUB_TOKEN = "fine-grained-token" # required for private PRs and publishing
```

Optional: set `OPENAI_BASE_URL` to change the API base URL. It defaults to `https://api.openai.com/v1/`.

Do not commit API keys. The `.gitignore` excludes `.env`, but this application deliberately does not load `.env` files automatically.

## Run a review

Pass a single UTF-8 source file (maximum 512 KiB):

```powershell
dotnet run --project src/AiCodeReviewer.Cli -- review-file "C:\path\to\example.py"
```

Run without a path to receive an interactive path prompt:

```powershell
dotnet run --project src/AiCodeReviewer.Cli
```

The command prints each finding's severity, category, explanation, location (when available), and suggested improvement. It also prints input, output, and total token counts when the provider returns them.

### Review a local repository

```powershell
dotnet run --project src/AiCodeReviewer.Cli -- review-repo "C:\path\to\repository"
```

The defaults review at most 50 supported files and approximately 250,000 source characters. Override either budget explicitly:

```powershell
dotnet run --project src/AiCodeReviewer.Cli -- review-repo . --max-files 20 --max-chars 100000
```

Review an HTTPS remote Git repository using an isolated shallow checkout:

```powershell
dotnet run --project src/AiCodeReviewer.Cli -- review-remote https://github.com/OWNER/REPOSITORY.git --max-files 20
```

Remote credentials must come from Git Credential Manager; credential-bearing URLs are rejected. The temporary checkout is deleted after the review, including when review execution fails.

Repository results are grouped by file and category. The summary reports files discovered, reviewed, skipped, and failed; source characters reviewed; and aggregate provider token usage when available.

Common irrelevant directories—including `.git`, `bin`, `obj`, `node_modules`, `vendor`, `dist`, `build`, and `target`—are not traversed. Generated/designer/minified files are filtered. Symbolic-link/reparse-point directories and files are skipped to keep traversal inside the selected tree.

### Review with repository intelligence (RAG)

```powershell
dotnet run --project src/AiCodeReviewer.Cli -- review-rag "C:\path\to\repository" --max-files 20 --max-chars 100000
```

`review-repo` remains available as the Version 3 single-file-context baseline. `review-rag` performs the Version 4 pipeline and reports chunks indexed, context chunks retrieved, embedding tokens, and review-model tokens.

### Agentic repository review

```powershell
dotnet run --project src/AiCodeReviewer.Cli -- review-agent "C:\path\to\repository"
```

The agent can call `list_files`, `read_file`, `search_code`, `find_symbol`, `find_references`, and `inspect_project_structure`. The CLI prints the investigation sequence before token usage.

### Review a GitHub pull request

Dry-run first; this retrieves the PR and prints draft comments without modifying GitHub:

```powershell
dotnet run --project src/AiCodeReviewer.Cli -- review-pr OWNER REPOSITORY 42
```

After a human reviews the drafts, rerun with explicit approval:

```powershell
dotnet run --project src/AiCodeReviewer.Cli -- review-pr OWNER REPOSITORY 42 --approve
```

`--approve` publishes one `COMMENT` review with inline comments on added lines. `GITHUB_TOKEN` needs pull-request read permission for private repositories and write permission to publish. It is read only from the environment.

### Specialist review

```powershell
dotnet run --project src/AiCodeReviewer.Cli -- review-specialists "C:\path\to\Example.cs"
```

The command runs bounded correctness, security, performance, architecture/maintainability, and testing reviews. It prints a unified severity-ordered report, the specialists responsible for each finding, and aggregate token usage.

## Supported languages

| Language | Extensions |
|---|---|
| C# | `.cs` |
| Python | `.py` |
| JavaScript | `.js`, `.jsx`, `.mjs`, `.cjs` |
| TypeScript | `.ts`, `.tsx`, `.mts`, `.cts` |
| Java | `.java` |
| C | `.c`, `.h` |
| C++ | `.cc`, `.cpp`, `.cxx`, `.hh`, `.hpp`, `.hxx` |

Detection is intentionally deterministic and based on the extension. Ambiguous `.h` files are treated as C in Version 2.

## Architecture

The solution separates policy, provider transport, and presentation:

- `AiCodeReviewer.Core` contains provider-neutral review, chunking, embedding, vector-index, repository, and retrieval abstractions plus an in-memory cosine-similarity index.
- `AiCodeReviewer.OpenAI` implements structured review and embedding providers using the OpenAI Responses and Embeddings APIs.
- `AiCodeReviewer.GitHub` isolates GitHub REST transport, unified-diff parsing, changed-line mapping, draft generation, and the approval guard.
- The Git infrastructure also provides validated HTTPS shallow checkouts for whole remote-repository reviews.
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

Version 7 demonstrates multi-agent specialization and orchestration. Specialists receive narrow, non-overlapping remits rather than five identical generic prompts. The coordinator groups equivalent findings, chooses the strongest representative, records every contributing specialist, orders the unified result, and aggregates usage.

```text
                         ┌─ Correctness reviewer ─┐
                         ├─ Security reviewer ────┤
Source + shared context ─┼─ Performance reviewer ┼─> Coordinator ─> deduplicated attributed report
                         ├─ Architecture reviewer ┤
                         └─ Testing reviewer ─────┘
```

Compared with the single-reviewer path, specialists can improve coverage and make responsibility clearer, but they require up to five model calls. That normally increases latency and token/API cost. The CLI reports aggregate usage so the tradeoff can be measured rather than assumed. `review-file` remains the baseline for an A/B comparison on the same input.

Safeguards are enforced by the application: repository-relative paths only, read-only operations, ignored dependency/build directories, a 500-line maximum per read, bounded search results, truncated tool output, a 12-call default budget, stateless API replay, and a visible audit log.

The benefit over Version 3 is that the model can see likely contracts, implementations, callers, or related classes without receiving every unrelated file. Use `review-repo` and `review-rag` on the same repository to compare their findings and token usage.

## Known limitations

- Retrieval improves cross-file context but does not guarantee that every syntactic dependency is found.
- The in-memory index is rebuilt for each run and is not a durable vector database.
- Chunking is language-aware in labeling but currently uses bounded line/blank-boundary heuristics rather than full language parsers.
- Symbol and reference tools use bounded textual search rather than compiler-grade semantic analysis.
- Agent behavior is probabilistic and may investigate different files between runs.
- Diff review reconstructs changed hunks rather than a complete checkout; very complex cross-file changes may need the local agent workflow for deeper context.
- Findings without a reliable added-line mapping remain in the report but are not drafted as inline GitHub comments.
- Deduplication uses category, file, location, and explanation similarity keys; semantically identical findings with very different wording can remain separate.
- Five specialist calls cost more and usually take longer than one generic call.
- Language detection is extension-based; it does not inspect file contents or disambiguate mixed-language files.
- Results are probabilistic and require human verification.
- Token counts are shown, but monetary cost is not calculated because pricing varies by model and can change.
- The CLI does not retry rate limits or transient failures.
- The maximum file-size check is a safety guard, not a model-specific token-budget calculation.

Version 7 completes the planned learning progression. Future improvements could add compiler-backed symbol analysis, durable indexes, evaluation datasets, and richer UIs without changing the provider boundaries.
