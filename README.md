# MonadicSharp.AI

[![NuGet Version](https://img.shields.io/nuget/v/MonadicSharp.AI.svg)](https://www.nuget.org/packages/MonadicSharp.AI/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/MonadicSharp.AI.svg)](https://www.nuget.org/packages/MonadicSharp.AI/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)

AI-specific extensions for [MonadicSharp](https://www.nuget.org/packages/MonadicSharp/). Typed error handling, exponential backoff retry, execution tracing, structured output validation, and streaming — all composable with `Result<T>`.

Topics:
csharp dotnet llm anthropic claude ai-integration error-handling retry nuget dotnet8

```bash
dotnet add package MonadicSharp.AI
```

---

## The problem

LLM APIs fail constantly — rate limits, timeouts, malformed JSON, content filters. The standard approach:

```csharp
// Exception-based: you don't know what can fail or when
try
{
    var response = await kernel.InvokePromptAsync(prompt);
    var data = JsonSerializer.Deserialize<ProductInfo>(response.ToString());

    if (data.Price <= 0) throw new ValidationException("Invalid price");

    return data;
}
catch (HttpRequestException ex) when (ex.StatusCode == 429)    { /* rate limit */ }
catch (TaskCanceledException)                                   { /* timeout */ }
catch (JsonException)                                           { /* bad output */ }
catch (ValidationException)                                     { /* domain rule */ }
catch (Exception)                                               { /* everything else */ }
```

Errors are invisible in method signatures. Composing multiple LLM calls is a catch-nesting nightmare. Retry logic is scattered everywhere. You have no traceability across multi-step pipelines.

## The solution

```csharp
var result = await Result.TryAsync(() => llm.CompleteAsync(prompt))
    .WithRetry(maxAttempts: 3)
    .ParseAs<ProductInfo>()
    .Validate(p => p.Price > 0, "Price must be positive")
    .AsResultAsync();

result.Match(
    onSuccess: p   => Console.WriteLine($"Got: {p.Name} at {p.Price}"),
    onFailure: err => Console.WriteLine($"Failed [{err.Code}]: {err.Message}")
);
```

Every failure is typed, propagated automatically, and handled in one place.

---

## Core types

### `AiError` — typed errors for LLM APIs

Instead of catching `HttpRequestException` and inspecting status codes, use semantic error factories:

```csharp
// Factory methods with structured metadata
AiError.RateLimit(retryAfter: TimeSpan.FromSeconds(30))
AiError.ModelTimeout(model: "claude-3-5-sonnet", elapsed: TimeSpan.FromSeconds(10))
AiError.ModelUnavailable(model: "gpt-4o", httpStatusCode: 503)
AiError.InvalidStructuredOutput(targetType: "ProductInfo", rawOutput: responseText)
AiError.TokenLimitExceeded(tokenCount: 128_500, limit: 128_000)
AiError.ContentFiltered(reason: "Policy violation")
AiError.AgentStepFailed(stepName: "Retrieve", innerError: dbError)
AiError.CircuitOpen(circuitName: "OpenAI")

// Retry policy — no magic strings
bool shouldRetry = AiError.IsRetriable(error);   // true for: RateLimit, Timeout, Unavailable, CircuitOpen
bool isTerminal  = AiError.IsTerminal(error);    // true for: Validation, ContentFiltered, TokenLimit
```

---

### `RetryResult<T>` — exponential backoff with observability

Wraps `Result<T>` with retry metadata. Only retries errors flagged as `IsRetriable` — no wasted attempts on `ContentFiltered` or `TokenLimitExceeded`.

```csharp
RetryResult<string> retryResult = await Result.TryAsync(() => llm.CompleteAsync(prompt))
    .WithRetry(maxAttempts: 3, initialDelay: TimeSpan.FromSeconds(1), useJitter: true);

Console.WriteLine($"Attempts: {retryResult.AttemptCount}");         // e.g. 2
Console.WriteLine($"Last transient error: {retryResult.LastAttemptError?.Code}"); // AI_RATE_LIMIT

// Transparent — use wherever Result<T> is expected
Result<string> result = retryResult;
```

**Backoff behaviour:** `delay * 2^(attempt-1)`, capped at 30s, ±25% jitter by default.

```csharp
// Retry a factory (new HTTP call each attempt)
var result = await ((Func<Task<Result<string>>>)(() =>
    Result.TryAsync(() => llm.CompleteAsync(prompt))
)).WithRetry(maxAttempts: 3, initialDelay: TimeSpan.FromSeconds(2));

// Retry inside a pipeline (BindWithRetry)
var result = await GetPromptAsync()
    .BindWithRetry(prompt => Result.TryAsync(() => llm.CompleteAsync(prompt)), attempts: 3);
```

---

### `ValidatedResult<T>` — parse and validate LLM output in one step

LLMs return strings. `ValidatedResult<T>` deserializes JSON and runs domain rules in a single composable chain. Parsing errors and validation errors are reported separately.

```csharp
// From a raw string
Result<ProductInfo> result = rawLlmResponse
    .ParseAs<ProductInfo>()
    .Validate(p => p.Price > 0,                   "Price must be positive")
    .Validate(p => !string.IsNullOrEmpty(p.Name), "Name is required")
    .Validate(p => p.Stock >= 0,                  "Stock cannot be negative")
    .AsResult();

// From a Result<string> (propagates upstream failures automatically)
Result<ProductInfo> result = await llmCallResult
    .ParseAs<ProductInfo>()
    .Validate(p => p.Price > 0, "Price must be positive")
    .AsResultAsync();

result.Match(
    onSuccess: p   => Save(p),
    onFailure: err => err.Type switch
    {
        ErrorType.Validation => ReturnValidationErrors(err),
        _                    => ReturnParsingError(err)
    }
);
```

---

### `AgentResult<TOutput>` — traced multi-step pipelines

For multi-step agent workflows, `AgentResult` carries both the final output and a full execution trace — timing, token counts, which step failed and why. The trace is always available, even on failure.

```csharp
AgentResult<string> result = await AgentResult
    .StartTrace("RAGPipeline", userQuery)
    .Step("Retrieve",  query   => vectorDb.SearchAsync(query))
    .Step("Rerank",    chunks  => reranker.RerankAsync(chunks))
    .Step("Generate",  context => llm.GenerateAsync(context))
    .ExecuteAsync();

// Output
result.Match(
    onSuccess: answer => Console.WriteLine(answer),
    onFailure: err    => Console.WriteLine($"Failed at step: {err.Metadata["StepName"]}")
);

// Full trace — always populated up to the point of failure
AgentExecutionTrace trace = result.Trace;
Console.WriteLine($"Steps:  {trace.Steps.Count}");
Console.WriteLine($"Tokens: {trace.TotalTokens}");
Console.WriteLine($"Time:   {trace.TotalDuration.TotalSeconds:F2}s");

foreach (var step in trace.Steps)
    Console.WriteLine($"  [{step.Name}] {step.Duration.TotalMilliseconds:F0}ms — {(step.Succeeded ? "OK" : step.Error!.Code)}");
```

For steps that report token usage:

```csharp
.Step("Generate", context => llm.GenerateWithMetricsAsync(context)
    .ContinueWith(t => (t.Result.result, t.Result.promptTokens, t.Result.completionTokens)))
```

---

### `StreamResult` — streaming completions in a monadic context

Wraps `IAsyncEnumerable<string>` to handle mid-stream errors and cancellation without throwing.

```csharp
// Collect full text with token-by-token callback
Result<string> result = await llm.StreamAsync(prompt)
    .ToStreamResult()
    .OnToken(token => Console.Write(token))     // live output while collecting
    .OnError(ex   => logger.LogError(ex, "Stream error"))
    .CollectAsync();

result.Match(
    onSuccess: text => Save(text),
    onFailure: err  => Console.WriteLine($"Stream failed: {err.Code}")
);

// Mid-stream errors are captured — never thrown
// Cancellation becomes Result.Failure("AI_STREAM_CANCELLED")
```

For scenarios where you need real-time access to each token AND the final result:

```csharp
await foreach (var (token, isLast, finalResult) in stream.StreamWithResultAsync())
{
    if (!isLast)
        await websocket.SendAsync(token);
    else
        await db.SaveAsync(finalResult!.Value);
}
```

---

## Combining everything

A realistic RAG pipeline with retry, validation, and tracing:

```csharp
AgentResult<OrderSummary> result = await AgentResult
    .StartTrace("OrderSummaryPipeline", orderId)
    .Step("Fetch", id =>
        Result.TryAsync(() => db.GetOrderAsync(id))
              .WithRetry(maxAttempts: 2))
    .Step("Summarize", order =>
        ((Func<Task<Result<string>>>)(() =>
            Result.TryAsync(() => llm.SummarizeAsync(order.ToString()))))
        .WithRetry(maxAttempts: 3, initialDelay: TimeSpan.FromSeconds(1)))
    .Step("Parse", summary =>
        Task.FromResult(
            summary.ParseAs<OrderSummary>()
                   .Validate(s => s.Total > 0, "Total must be positive")
                   .AsResult()))
    .ExecuteAsync();

result.Match(
    onSuccess: s   => Console.WriteLine($"Summary: {s.Title} — ${s.Total}"),
    onFailure: err => Console.WriteLine($"[{err.Code}] {err.Message}")
);

// Trace always available
logger.LogInformation("Pipeline completed in {Ms}ms using {Tokens} tokens",
    result.Trace.TotalDuration.TotalMilliseconds,
    result.Trace.TotalTokens);
```

---

## Semantic Kernel bridge

To integrate with Microsoft Semantic Kernel, add the package to your project and define the `SEMANTIC_KERNEL` compilation symbol:

```xml
<PackageReference Include="Microsoft.SemanticKernel" Version="1.*" />
<DefineConstants>SEMANTIC_KERNEL</DefineConstants>
```

Then use the bridge extensions:

```csharp
// FunctionResult → Result<string>
Result<string> result = kernelFunctionResult.ToResult();

// Invoke a KernelFunction → Result<string>
Result<string> result = await kernel.InvokeAsResultAsync(myFunction, args);

// Invoke a prompt → Result<string>
Result<string> result = await kernel.InvokePromptAsResultAsync("Summarize: {{$input}}", args);

// Stream a prompt → StreamResult
Result<string> full = await kernel.StreamPromptAsResult("Write a report on {{$topic}}", args)
    .OnToken(t => Console.Write(t))
    .CollectAsync();
```

---

## Why this exists

| Problem | MonadicSharp.AI |
|---------|-----------------|
| LLM API error types scattered in catch blocks | `AiError` — typed factory methods, `IsRetriable()` |
| Retry logic duplicated across every call site | `WithRetry` / `BindWithRetry` — one config, used everywhere |
| No visibility into multi-step agent failures | `AgentResult` — trace preserved up to the point of failure |
| JSON parsing + validation in separate steps | `ValidatedResult` — one composable chain |
| Streaming errors throw unexpectedly | `StreamResult` — errors become `Result.Failure`, never rethrown |

The [failure compounding problem](https://arxiv.org/abs/2307.13528) in multi-step agents: a pipeline with 95% per-step reliability reaches only 36% end-to-end reliability across 20 steps. Railway-Oriented Programming makes each failure explicit, composable, and recoverable.

---

## Requirements

- [MonadicSharp](https://www.nuget.org/packages/MonadicSharp/) ≥ 1.4.0
- .NET 8.0+
- C# 12+

## License

MIT — see [LICENSE](../LICENSE).
