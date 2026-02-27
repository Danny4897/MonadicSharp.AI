using System.Text.Json;
using MonadicSharp.AI.Errors;

namespace MonadicSharp.AI;

/// <summary>
/// Fluent builder for parsing and validating structured LLM output.
/// Combines JSON deserialization with domain rule validation in a single composable pipeline.
/// <code>
/// var result = await llmResponse
///     .ParseAs&lt;ProductInfo&gt;()
///     .Validate(p => p.Price > 0,               "Price must be positive")
///     .Validate(p => !string.IsNullOrEmpty(p.Name), "Name is required")
///     .AsResult();
/// </code>
/// </summary>
public sealed class ValidatedResult<T>
{
    private readonly Result<T> _parsed;
    private readonly List<(Func<T, bool> predicate, string message)> _rules = new();

    internal ValidatedResult(Result<T> parsed) => _parsed = parsed;

    /// <summary>Adds a domain validation rule.</summary>
    public ValidatedResult<T> Validate(Func<T, bool> predicate, string errorMessage)
    {
        _rules.Add((predicate, errorMessage));
        return this;
    }

    /// <summary>
    /// Runs all validation rules and returns the final <see cref="Result{T}"/>.
    /// Parsing errors and validation errors are reported separately via ErrorType.
    /// </summary>
    public Result<T> AsResult()
    {
        if (_parsed.IsFailure)
            return _parsed;

        var value = _parsed.Value;
        var failures = _rules
            .Where(r => !r.predicate(value))
            .Select(r => Error.Validation(r.message))
            .ToArray();

        return failures.Length == 0
            ? _parsed
            : Result<T>.Failure(failures.Length == 1 ? failures[0] : Error.Combine(failures));
    }

    /// <summary>Async convenience — awaitable version of <see cref="AsResult"/>.</summary>
    public Task<Result<T>> AsResultAsync() => Task.FromResult(AsResult());
}

/// <summary>Extension methods for building <see cref="ValidatedResult{T}"/> pipelines.</summary>
public static class ValidatedResultExtensions
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Attempts to deserialize the string as <typeparamref name="T"/>.
    /// Returns a <see cref="ValidatedResult{T}"/> builder for chaining validation rules.
    /// </summary>
    public static ValidatedResult<T> ParseAs<T>(this string json)
    {
        try
        {
            var value = JsonSerializer.Deserialize<T>(json, _jsonOptions);
            var result = value is not null
                ? Result<T>.Success(value)
                : Result<T>.Failure(AiError.InvalidStructuredOutput(typeof(T).Name, json));

            return new ValidatedResult<T>(result);
        }
        catch (JsonException ex)
        {
            var error = AiError.InvalidStructuredOutput(typeof(T).Name, json)
                               .WithMetadata("JsonException", ex.Message);
            return new ValidatedResult<T>(Result<T>.Failure(error));
        }
    }

    /// <summary>
    /// Parses a <see cref="Result{String}"/> LLM response into a typed object.
    /// Propagates upstream failures without attempting deserialization.
    /// </summary>
    public static ValidatedResult<T> ParseAs<T>(this Result<string> llmResponse)
    {
        if (llmResponse.IsFailure)
            return new ValidatedResult<T>(Result<T>.Failure(llmResponse.Error));

        return llmResponse.Value.ParseAs<T>();
    }

    /// <summary>Async variant for use in pipeline chains.</summary>
    public static async Task<ValidatedResult<T>> ParseAs<T>(this Task<Result<string>> llmResponseTask)
    {
        var response = await llmResponseTask.ConfigureAwait(false);
        return response.ParseAs<T>();
    }
}
