using MonadicSharp.AI.Errors;

namespace MonadicSharp.AI.Recovery;

/// <summary>
/// Typed predicate factory for AI errors, designed to plug directly into
/// <c>MonadicSharp.Recovery</c>'s <c>RescueAsync</c> and <c>StartFixBranchAsync</c> operators.
/// </summary>
/// <remarks>
/// These predicates return <c>Func&lt;Error, bool&gt;</c> and are composable
/// with <c>MonadicSharp.Recovery.ErrorPredicates</c> using <c>.Or()</c> and <c>.And()</c>.
/// </remarks>
/// <example>
/// With MonadicSharp.Recovery installed:
/// <code>
/// await pipeline.StartFixBranchAsync(
///     when:     AiRecoveryPredicates.InvalidOutput(),
///     recovery: (error, attempt) => RepairAsync(error, attempt));
///
/// // Compose with Recovery predicates
/// await pipeline.RescueAsync(
///     when: AiRecoveryPredicates.Transient()
///               .Or(AiRecoveryPredicates.InvalidOutput()),
///     recovery: error => RetryAsync(error));
/// </code>
/// </example>
public static class AiRecoveryPredicates
{
    /// <summary>
    /// Matches errors where <see cref="AiError.IsRetriable"/> is true:
    /// rate limit, timeout, model unavailable, circuit open.
    /// </summary>
    public static Func<Error, bool> Transient() =>
        e => AiError.IsRetriable(e);

    /// <summary>
    /// Matches <see cref="AiError.InvalidOutputCode"/> — malformed or unparseable structured output.
    /// This is the primary predicate for self-healing JSON repair.
    /// </summary>
    public static Func<Error, bool> InvalidOutput() =>
        e => e.HasCode(AiError.InvalidOutputCode);

    /// <summary>
    /// Matches rate limit errors only (<see cref="AiError.RateLimitCode"/>).
    /// Use when you want to rescue rate limits with a different backoff strategy.
    /// </summary>
    public static Func<Error, bool> RateLimit() =>
        e => e.HasCode(AiError.RateLimitCode);

    /// <summary>
    /// Matches timeout errors only (<see cref="AiError.ModelTimeoutCode"/>).
    /// </summary>
    public static Func<Error, bool> Timeout() =>
        e => e.HasCode(AiError.ModelTimeoutCode);

    /// <summary>
    /// Matches model unavailability (<see cref="AiError.ModelUnavailableCode"/>).
    /// </summary>
    public static Func<Error, bool> ModelUnavailable() =>
        e => e.HasCode(AiError.ModelUnavailableCode);

    /// <summary>
    /// Matches all transient errors AND invalid output — the broadest useful predicate
    /// for an AI self-healing pipeline.
    /// </summary>
    public static Func<Error, bool> TransientOrInvalidOutput() =>
        e => AiError.IsRetriable(e) || e.HasCode(AiError.InvalidOutputCode);

    /// <summary>
    /// Matches errors that are NOT terminal — i.e. retrying makes sense.
    /// Equivalent to <see cref="Transient"/> but named for readability in fix-branch contexts.
    /// </summary>
    public static Func<Error, bool> IsRecoverable() =>
        e => !AiError.IsTerminal(e);
}
