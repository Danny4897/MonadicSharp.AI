using FluentAssertions;

namespace MonadicSharp.AI.Tests;

public class StreamResultTests
{
    private static async IAsyncEnumerable<string> TokenStream(params string[] tokens)
    {
        foreach (var token in tokens)
        {
            await Task.Yield();
            yield return token;
        }
    }

    private static async IAsyncEnumerable<string> ThrowingStream()
    {
        await Task.Yield();
        yield return "partial ";
        throw new InvalidOperationException("mid-stream failure");
    }

    [Fact]
    public async Task CollectAsync_AccumulatesAllTokens()
    {
        var result = await TokenStream("Hello", " ", "world", "!")
            .ToStreamResult()
            .CollectAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Hello world!");
    }

    [Fact]
    public async Task CollectAsync_InvokesOnTokenCallback()
    {
        var seen = new List<string>();

        var result = await TokenStream("A", "B", "C")
            .ToStreamResult()
            .OnToken(t => seen.Add(t))
            .CollectAsync();

        result.IsSuccess.Should().BeTrue();
        seen.Should().Equal("A", "B", "C");
    }

    [Fact]
    public async Task CollectAsync_MidStreamException_ReturnsFailure()
    {
        var result = await ThrowingStream()
            .ToStreamResult()
            .CollectAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AI_STREAM_ERROR");
    }

    [Fact]
    public async Task CollectAsync_MidStreamException_InvokesOnErrorCallback()
    {
        Exception? captured = null;

        await ThrowingStream()
            .ToStreamResult()
            .OnError(ex => captured = ex)
            .CollectAsync();

        captured.Should().NotBeNull();
        captured.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task CollectAsync_Cancellation_ReturnsCancelledError()
    {
        var cts = new CancellationTokenSource();

        // Stream that yields forever
        async IAsyncEnumerable<string> Infinite(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(1, ct);
                yield return "x";
            }
        }

        cts.CancelAfter(50);
        var result = await Infinite()
            .ToStreamResult()
            .CollectAsync(cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AI_STREAM_CANCELLED");
    }

    [Fact]
    public async Task CollectAsync_EmptyStream_ReturnsEmptyString()
    {
        var result = await TokenStream()
            .ToStreamResult()
            .CollectAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
