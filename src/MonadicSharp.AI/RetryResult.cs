using MonadicSharp.AI.Errors;

namespace MonadicSharp.AI;

/// <summary>
/// Wraps a <see cref="Result{T}"/> with retry observability metadata.
/// Carries the final outcome plus attempt count and the last transient error
/// encountered during retries, making retry behavior transparent and testable.
/// </summary>
public sealed class RetryResult<T>
{
    /// <summary>The final outcome after all retry attempts.</summary>
    public Result<T> Result { get; }

    /// <summary>Number of attempts made (1 = succeeded or failed on first try).</summary>
    public int AttemptCount { get; }

    /// <summary>The last transient error before a successful retry, if any.</summary>
    public Error? LastAttemptError { get; }

    /// <summary>True if the operation eventually succeeded.</summary>
    public bool IsSuccess => Result.IsSuccess;

    /// <summary>True if all attempts were exhausted without success.</summary>
    public bool IsFailure => Result.IsFailure;

    internal RetryResult(Result<T> result, int attemptCount, Error? lastAttemptError = null)
    {
        Result         = result;
        AttemptCount   = attemptCount;
        LastAttemptError = lastAttemptError;
    }

    /// <summary>Transparent conversion — use RetryResult anywhere Result is expected.</summary>
    public static implicit operator Result<T>(RetryResult<T> retryResult) => retryResult.Result;

    public override string ToString() =>
        $"RetryResult(attempts={AttemptCount}, {Result})";
}
