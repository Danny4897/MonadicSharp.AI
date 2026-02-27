using MonadicSharp.AI.Errors;

namespace MonadicSharp.AI.Extensions;

/// <summary>
/// AI-specific pipeline extensions for <see cref="Result{T}"/> and <see cref="Task{T}"/>.
/// Adds exponential backoff retry with jitter, aware of which errors are retriable.
/// </summary>
public static class AiPipelineExtensions
{
    /// <summary>
    /// Retries the entire <paramref name="resultTask"/> operation with exponential backoff.
    /// Only retries on errors where <see cref="AiError.IsRetriable"/> is true.
    /// </summary>
    /// <param name="resultTask">The operation to retry.</param>
    /// <param name="maxAttempts">Maximum total attempts (default 3).</param>
    /// <param name="initialDelay">Delay before the second attempt (default 1s).</param>
    /// <param name="useJitter">Add random jitter to avoid thundering herd (default true).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task<RetryResult<T>> WithRetry<T>(
        this Task<Result<T>> resultTask,
        int maxAttempts = 3,
        TimeSpan? initialDelay = null,
        bool useJitter = true,
        CancellationToken cancellationToken = default)
    {
        return WithRetryCore(
            () => resultTask,
            maxAttempts,
            initialDelay ?? TimeSpan.FromSeconds(1),
            useJitter,
            cancellationToken);
    }

    /// <summary>
    /// Retries a factory <paramref name="operation"/> with exponential backoff.
    /// Use this overload when the operation needs to be re-invoked on each attempt
    /// (e.g. a new HTTP request each time).
    /// </summary>
    public static Task<RetryResult<T>> WithRetry<T>(
        this Func<Task<Result<T>>> operation,
        int maxAttempts = 3,
        TimeSpan? initialDelay = null,
        bool useJitter = true,
        CancellationToken cancellationToken = default)
    {
        return WithRetryCore(operation, maxAttempts, initialDelay ?? TimeSpan.FromSeconds(1), useJitter, cancellationToken);
    }

    /// <summary>
    /// Binds a value through an <paramref name="operation"/> with retry on transient failures.
    /// Short-circuits immediately if the incoming result is already a failure.
    /// </summary>
    public static async Task<RetryResult<TOut>> BindWithRetry<TIn, TOut>(
        this Task<Result<TIn>> pipeline,
        Func<TIn, Task<Result<TOut>>> operation,
        int attempts = 3,
        TimeSpan? initialDelay = null,
        bool useJitter = true,
        CancellationToken cancellationToken = default)
    {
        var incoming = await pipeline.ConfigureAwait(false);
        if (incoming.IsFailure)
            return new RetryResult<TOut>(Result<TOut>.Failure(incoming.Error), attemptCount: 0);

        return await WithRetryCore(
            () => operation(incoming.Value),
            attempts,
            initialDelay ?? TimeSpan.FromSeconds(1),
            useJitter,
            cancellationToken).ConfigureAwait(false);
    }

    // ── Core retry logic ─────────────────────────────────────────────────────

    private static async Task<RetryResult<T>> WithRetryCore<T>(
        Func<Task<Result<T>>> operation,
        int maxAttempts,
        TimeSpan initialDelay,
        bool useJitter,
        CancellationToken cancellationToken)
    {
        if (maxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Must be at least 1.");

        Error? lastError = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await operation().ConfigureAwait(false);

            if (result.IsSuccess)
                return new RetryResult<T>(result, attempt, lastError);

            lastError = result.Error;

            // Non-retriable → fail immediately, no point waiting
            if (AiError.IsTerminal(result.Error) || attempt == maxAttempts)
                return new RetryResult<T>(result, attempt, lastError);

            var delay = CalculateDelay(initialDelay, attempt, useJitter);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        // Unreachable — kept to satisfy compiler
        return new RetryResult<T>(Result<T>.Failure(Error.Create("Unexpected retry exit.")), maxAttempts, lastError);
    }

    /// <summary>Exponential backoff: delay * 2^(attempt-1), capped at 30s, optional jitter ±25%.</summary>
    private static TimeSpan CalculateDelay(TimeSpan initialDelay, int attempt, bool useJitter)
    {
        // attempt 1 → initialDelay, attempt 2 → 2x, attempt 3 → 4x …
        var exponential = TimeSpan.FromMilliseconds(initialDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
        var capped      = exponential < TimeSpan.FromSeconds(30) ? exponential : TimeSpan.FromSeconds(30);

        if (!useJitter)
            return capped;

        var jitterFactor = 0.75 + Random.Shared.NextDouble() * 0.5; // 0.75 – 1.25
        return TimeSpan.FromMilliseconds(capped.TotalMilliseconds * jitterFactor);
    }
}
