namespace MonadicSharp.AI.Errors;

/// <summary>
/// Factory methods for AI-specific errors with typed codes for LLM API failures.
/// Use <see cref="IsRetriable"/> to determine whether a retry attempt makes sense.
/// </summary>
public static class AiError
{
    // ── Error codes ──────────────────────────────────────────────────────────

    public const string RateLimitCode         = "AI_RATE_LIMIT";
    public const string ModelTimeoutCode      = "AI_MODEL_TIMEOUT";
    public const string ModelUnavailableCode  = "AI_MODEL_UNAVAILABLE";
    public const string InvalidOutputCode     = "AI_INVALID_STRUCTURED_OUTPUT";
    public const string TokenLimitCode        = "AI_TOKEN_LIMIT_EXCEEDED";
    public const string ContentFilteredCode   = "AI_CONTENT_FILTERED";
    public const string AgentStepFailedCode   = "AI_AGENT_STEP_FAILED";
    public const string CircuitOpenCode       = "AI_CIRCUIT_OPEN";

    // ── Factory methods ──────────────────────────────────────────────────────

    /// <summary>HTTP 429 — LLM API rate limit hit.</summary>
    public static Error RateLimit(string? message = null, TimeSpan? retryAfter = null)
    {
        var error = Error.Create(message ?? "LLM API rate limit exceeded.", RateLimitCode);
        return retryAfter.HasValue
            ? error.WithMetadata("RetryAfterSeconds", (int)retryAfter.Value.TotalSeconds)
            : error;
    }

    /// <summary>LLM inference request timed out.</summary>
    public static Error ModelTimeout(string? model = null, TimeSpan? elapsed = null)
    {
        var error = Error.Create($"LLM model{(model != null ? $" '{model}'" : "")} timed out.", ModelTimeoutCode);
        return elapsed.HasValue
            ? error.WithMetadata("ElapsedMs", (long)elapsed.Value.TotalMilliseconds)
            : error;
    }

    /// <summary>LLM provider is temporarily unavailable (5xx).</summary>
    public static Error ModelUnavailable(string? model = null, int? httpStatusCode = null)
    {
        var error = Error.Create($"LLM model{(model != null ? $" '{model}'" : "")} is unavailable.", ModelUnavailableCode);
        return httpStatusCode.HasValue
            ? error.WithMetadata("HttpStatusCode", httpStatusCode.Value)
            : error;
    }

    /// <summary>LLM returned malformed or unparseable structured output (JSON).</summary>
    public static Error InvalidStructuredOutput(string targetType, string? rawOutput = null)
    {
        var error = Error.Create(
            $"LLM response could not be parsed as '{targetType}'.",
            InvalidOutputCode,
            ErrorType.Validation);
        return rawOutput != null
            ? error.WithMetadata("RawOutput", rawOutput.Length > 500 ? rawOutput[..500] + "…" : rawOutput)
            : error;
    }

    /// <summary>Prompt + completion exceeded the model's context window.</summary>
    public static Error TokenLimitExceeded(int? tokenCount = null, int? limit = null)
    {
        var error = Error.Create("Token limit exceeded for this model.", TokenLimitCode);
        if (tokenCount.HasValue) error = error.WithMetadata("TokenCount", tokenCount.Value);
        if (limit.HasValue)      error = error.WithMetadata("ModelLimit", limit.Value);
        return error;
    }

    /// <summary>Response blocked by the model's safety/content filters.</summary>
    public static Error ContentFiltered(string? reason = null)
        => Error.Create(reason ?? "Response blocked by content filter.", ContentFilteredCode, ErrorType.Forbidden);

    /// <summary>A specific step in a multi-step agent pipeline failed.</summary>
    public static Error AgentStepFailed(string stepName, Error innerError)
        => Error.Create($"Agent step '{stepName}' failed.", AgentStepFailedCode)
                .WithMetadata("StepName", stepName)
                .WithInnerError(innerError);

    /// <summary>Circuit breaker is open — calls are being short-circuited.</summary>
    public static Error CircuitOpen(string? circuitName = null, TimeSpan? opensAt = null)
    {
        var error = Error.Create(
            $"Circuit breaker{(circuitName != null ? $" '{circuitName}'" : "")} is open.",
            CircuitOpenCode);
        return opensAt.HasValue
            ? error.WithMetadata("ResetsAtUtc", opensAt.Value.ToString())
            : error;
    }

    // ── Retry policy helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the error represents a transient condition that may
    /// succeed on a subsequent attempt (rate limit, timeout, unavailability).
    /// <para>Non-retriable: Validation, ContentFiltered, TokenLimitExceeded.</para>
    /// </summary>
    public static bool IsRetriable(Error error) => error.Code is
        RateLimitCode or
        ModelTimeoutCode or
        ModelUnavailableCode or
        CircuitOpenCode;

    /// <summary>Returns true if the error is NOT worth retrying.</summary>
    public static bool IsTerminal(Error error) => !IsRetriable(error);
}
