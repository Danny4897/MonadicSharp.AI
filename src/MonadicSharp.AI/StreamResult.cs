using System.Text;
using MonadicSharp.AI.Errors;

namespace MonadicSharp.AI;

/// <summary>
/// Wraps an <see cref="IAsyncEnumerable{T}"/> streaming completion in a monadic context.
/// Handles mid-stream errors, cancellation, and token accumulation without throwing.
/// <code>
/// var result = await llm.StreamAsync(prompt)
///     .ToStreamResult()
///     .OnToken(token => Console.Write(token))
///     .CollectAsync();
/// // result: Result&lt;string&gt; — full text or typed error, never an exception
/// </code>
/// </summary>
public sealed class StreamResult
{
    private readonly IAsyncEnumerable<string> _stream;
    private Action<string>? _onToken;
    private Action<Exception>? _onError;

    private StreamResult(IAsyncEnumerable<string> stream) => _stream = stream;

    /// <summary>Wraps an async token stream into a <see cref="StreamResult"/> builder.</summary>
    public static StreamResult From(IAsyncEnumerable<string> stream) => new(stream);

    /// <summary>Registers a callback invoked for each token as it arrives.</summary>
    public StreamResult OnToken(Action<string> callback)
    {
        _onToken = callback;
        return this;
    }

    /// <summary>Registers a callback invoked if a mid-stream error occurs.</summary>
    public StreamResult OnError(Action<Exception> callback)
    {
        _onError = callback;
        return this;
    }

    /// <summary>
    /// Consumes the stream to completion, accumulates all tokens, and returns the full text.
    /// Any exception during streaming is captured and returned as a typed <see cref="Error"/>.
    /// </summary>
    public async Task<Result<string>> CollectAsync(CancellationToken cancellationToken = default)
    {
        var buffer = new StringBuilder();

        try
        {
            await foreach (var token in _stream.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                buffer.Append(token);
                _onToken?.Invoke(token);
            }

            return Result<string>.Success(buffer.ToString());
        }
        catch (OperationCanceledException)
        {
            return Result<string>.Failure(
                Error.Create("Streaming was cancelled.", "AI_STREAM_CANCELLED", ErrorType.Failure));
        }
        catch (Exception ex)
        {
            _onError?.Invoke(ex);
            return Result<string>.Failure(Error.FromException(ex, "AI_STREAM_ERROR"));
        }
    }

    /// <summary>
    /// Consumes the stream and yields partial results token-by-token.
    /// Yields a final <see cref="Result{String}"/> with the full text or an error.
    /// </summary>
    public async IAsyncEnumerable<(string Token, bool IsLast, Result<string>? FinalResult)> StreamWithResultAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = new StringBuilder();
        Result<string>? finalResult = null;

        var enumerator = _stream.GetAsyncEnumerator(cancellationToken);
        // C# does not allow yield inside catch — capture error state and yield after the try/catch
        Result<string>? earlyExit = null;
        bool hasMore;

        try
        {
            while (true)
            {
                earlyExit = null;
                try
                {
                    hasMore = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    earlyExit = Result<string>.Failure(Error.Create("Streaming cancelled.", "AI_STREAM_CANCELLED"));
                    hasMore = false;
                }
                catch (Exception ex)
                {
                    _onError?.Invoke(ex);
                    earlyExit = Result<string>.Failure(Error.FromException(ex, "AI_STREAM_ERROR"));
                    hasMore = false;
                }

                if (earlyExit.HasValue)
                {
                    yield return ("", true, earlyExit.Value);
                    yield break;
                }

                if (!hasMore)
                {
                    finalResult = Result<string>.Success(buffer.ToString());
                    yield return ("", true, finalResult);
                    yield break;
                }

                var token = enumerator.Current;
                buffer.Append(token);
                _onToken?.Invoke(token);
                yield return (token, false, null);
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>Extension methods for creating <see cref="StreamResult"/> from async enumerables.</summary>
public static class StreamResultExtensions
{
    /// <summary>Wraps an <see cref="IAsyncEnumerable{String}"/> in a <see cref="StreamResult"/> builder.</summary>
    public static StreamResult ToStreamResult(this IAsyncEnumerable<string> stream)
        => StreamResult.From(stream);
}
