using System.Diagnostics;
using MonadicSharp.AI.Errors;

namespace MonadicSharp.AI;

// ── Trace records ────────────────────────────────────────────────────────────

/// <summary>Record of a single step executed within an agent pipeline.</summary>
public sealed record AgentStep
{
    public string Name        { get; init; } = string.Empty;
    public string? InputSummary  { get; init; }
    public string? OutputSummary { get; init; }
    public TimeSpan Duration  { get; init; }
    public int PromptTokens   { get; init; }
    public int CompletionTokens { get; init; }
    public bool Succeeded     { get; init; }
    public Error? Error       { get; init; }

    public int TotalTokens => PromptTokens + CompletionTokens;
}

/// <summary>
/// Full execution trace of an agent pipeline run.
/// Accumulates steps as they execute; accessible whether the pipeline succeeded or failed.
/// </summary>
public sealed class AgentExecutionTrace
{
    private readonly List<AgentStep> _steps = new();

    public string PipelineName { get; }
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; private set; }

    public IReadOnlyList<AgentStep> Steps => _steps;
    public TimeSpan TotalDuration => CompletedAt.HasValue ? CompletedAt.Value - StartedAt : TimeSpan.Zero;
    public int TotalPromptTokens     => _steps.Sum(s => s.PromptTokens);
    public int TotalCompletionTokens => _steps.Sum(s => s.CompletionTokens);
    public int TotalTokens           => _steps.Sum(s => s.TotalTokens);

    internal AgentExecutionTrace(string pipelineName) => PipelineName = pipelineName;

    internal void AddStep(AgentStep step) => _steps.Add(step);
    internal void Complete() => CompletedAt = DateTimeOffset.UtcNow;

    public override string ToString() =>
        $"Trace[{PipelineName}]: {_steps.Count} steps, {TotalTokens} tokens, {TotalDuration.TotalMilliseconds:F0}ms";
}

// ── Result type ──────────────────────────────────────────────────────────────

/// <summary>
/// Combines an operation's output with its full execution trace.
/// The trace is always available — even when the pipeline failed — enabling
/// post-mortem analysis of which step went wrong and why.
/// </summary>
public sealed class AgentResult<TOutput>
{
    public Result<TOutput> Result { get; }
    public AgentExecutionTrace Trace { get; }

    public bool IsSuccess => Result.IsSuccess;
    public bool IsFailure => Result.IsFailure;
    public TOutput Value  => Result.Value;
    public Error Error    => Result.Error;

    internal AgentResult(Result<TOutput> result, AgentExecutionTrace trace)
    {
        Result = result;
        Trace  = trace;
    }

    /// <summary>Transforms the output value while preserving the trace.</summary>
    public AgentResult<TNext> Map<TNext>(Func<TOutput, TNext> mapper) =>
        new(Result.Map(mapper), Trace);

    /// <summary>Transparent conversion to the underlying Result.</summary>
    public static implicit operator Result<TOutput>(AgentResult<TOutput> r) => r.Result;

    public override string ToString() => $"AgentResult({Result}) | {Trace}";
}

// ── Builder ──────────────────────────────────────────────────────────────────

/// <summary>
/// Fluent builder for traced agent pipelines.
/// <code>
/// var result = await AgentResult.StartTrace("RAGPipeline")
///     .Step("Retrieve", query  => vectorDb.SearchAsync(query))
///     .Step("Generate", chunks => llm.GenerateAsync(chunks))
///     .ExecuteAsync(userQuery);
/// </code>
/// </summary>
public static class AgentResult
{
    /// <summary>Starts a new traced pipeline with the given name.</summary>
    public static AgentResultBuilder<T> StartTrace<T>(string pipelineName, T initialValue) =>
        new(pipelineName, Task.FromResult(Result<T>.Success(initialValue)));

    /// <summary>Starts a pipeline where the initial value comes from an async operation.</summary>
    public static AgentResultBuilder<T> StartTrace<T>(string pipelineName, Func<Task<Result<T>>> initialOperation) =>
        new(pipelineName, initialOperation());
}

/// <summary>
/// Accumulates pipeline steps and executes them sequentially,
/// recording timing and token usage for each step.
/// </summary>
public sealed class AgentResultBuilder<TCurrent>
{
    private readonly string _pipelineName;
    private readonly Task<Result<TCurrent>> _current;
    private readonly AgentExecutionTrace _trace;
    private readonly List<Func<TCurrent, Task<(Result<TCurrent>, AgentStep)>>> _steps = new();

    internal AgentResultBuilder(string pipelineName, Task<Result<TCurrent>> current)
    {
        _pipelineName = pipelineName;
        _current      = current;
        _trace        = new AgentExecutionTrace(pipelineName);
    }

    private AgentResultBuilder(string pipelineName, Task<Result<TCurrent>> current, AgentExecutionTrace trace,
        List<Func<TCurrent, Task<(Result<TCurrent>, AgentStep)>>> steps)
    {
        _pipelineName = pipelineName;
        _current      = current;
        _trace        = trace;
        _steps        = steps;
    }

    /// <summary>Adds a named step that transforms the current value.</summary>
    public AgentResultBuilder<TCurrent> Step(
        string name,
        Func<TCurrent, Task<Result<TCurrent>>> operation,
        Func<TCurrent, string>? summarizeInput = null,
        Func<TCurrent, string>? summarizeOutput = null)
    {
        var newSteps = new List<Func<TCurrent, Task<(Result<TCurrent>, AgentStep)>>>(_steps)
        {
            async input =>
            {
                var sw = Stopwatch.StartNew();
                var result = await operation(input).ConfigureAwait(false);
                sw.Stop();

                var step = new AgentStep
                {
                    Name          = name,
                    InputSummary  = summarizeInput?.Invoke(input),
                    OutputSummary = result.IsSuccess ? summarizeOutput?.Invoke(result.Value) : null,
                    Duration      = sw.Elapsed,
                    Succeeded     = result.IsSuccess,
                    Error         = result.IsFailure ? result.Error : null
                };

                return (result, step);
            }
        };

        return new AgentResultBuilder<TCurrent>(_pipelineName, _current, _trace, newSteps);
    }

    /// <summary>Adds a step that also reports token usage.</summary>
    public AgentResultBuilder<TCurrent> Step(
        string name,
        Func<TCurrent, Task<(Result<TCurrent> result, int promptTokens, int completionTokens)>> operation)
    {
        var newSteps = new List<Func<TCurrent, Task<(Result<TCurrent>, AgentStep)>>>(_steps)
        {
            async input =>
            {
                var sw = Stopwatch.StartNew();
                var (result, promptTokens, completionTokens) = await operation(input).ConfigureAwait(false);
                sw.Stop();

                var step = new AgentStep
                {
                    Name             = name,
                    Duration         = sw.Elapsed,
                    PromptTokens     = promptTokens,
                    CompletionTokens = completionTokens,
                    Succeeded        = result.IsSuccess,
                    Error            = result.IsFailure ? result.Error : null
                };

                return (result, step);
            }
        };

        return new AgentResultBuilder<TCurrent>(_pipelineName, _current, _trace, newSteps);
    }

    /// <summary>
    /// Executes all steps sequentially. Stops at the first failure.
    /// The execution trace is always populated up to the point of failure.
    /// </summary>
    public async Task<AgentResult<TCurrent>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var current = await _current.ConfigureAwait(false);

        if (current.IsFailure)
        {
            _trace.Complete();
            return new AgentResult<TCurrent>(current, _trace);
        }

        var value = current.Value;

        foreach (var stepFn in _steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (result, step) = await stepFn(value).ConfigureAwait(false);
            _trace.AddStep(step);

            if (result.IsFailure)
            {
                _trace.Complete();
                var failedStep = AiError.AgentStepFailed(step.Name, result.Error);
                return new AgentResult<TCurrent>(Result<TCurrent>.Failure(failedStep), _trace);
            }

            value = result.Value;
        }

        _trace.Complete();
        return new AgentResult<TCurrent>(Result<TCurrent>.Success(value), _trace);
    }
}
