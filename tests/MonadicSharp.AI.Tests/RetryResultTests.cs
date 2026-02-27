using FluentAssertions;
using MonadicSharp.AI.Errors;
using MonadicSharp.AI.Extensions;

namespace MonadicSharp.AI.Tests;

public class RetryResultTests
{
    [Fact]
    public async Task WithRetry_SucceedsOnFirstAttempt_ReturnsAttemptCount1()
    {
        var retryResult = await Task.FromResult(Result<string>.Success("ok"))
            .WithRetry(maxAttempts: 3, initialDelay: TimeSpan.Zero, useJitter: false);

        retryResult.IsSuccess.Should().BeTrue();
        retryResult.AttemptCount.Should().Be(1);
        retryResult.LastAttemptError.Should().BeNull();
    }

    [Fact]
    public async Task WithRetry_SucceedsOnSecondAttempt_ReturnsCorrectCount()
    {
        int calls = 0;
        var retryResult = await ((Func<Task<Result<string>>>)(() =>
        {
            calls++;
            return Task.FromResult(calls < 2
                ? Result<string>.Failure(AiError.RateLimit())
                : Result<string>.Success("ok"));
        })).WithRetry(maxAttempts: 3, initialDelay: TimeSpan.Zero, useJitter: false);

        retryResult.IsSuccess.Should().BeTrue();
        retryResult.AttemptCount.Should().Be(2);
        retryResult.LastAttemptError.Should().NotBeNull();
        retryResult.LastAttemptError!.Code.Should().Be(AiError.RateLimitCode);
    }

    [Fact]
    public async Task WithRetry_ExhaustsAttempts_ReturnsFailure()
    {
        var retryResult = await ((Func<Task<Result<string>>>)(() =>
            Task.FromResult(Result<string>.Failure(AiError.ModelTimeout()))
        )).WithRetry(maxAttempts: 3, initialDelay: TimeSpan.Zero, useJitter: false);

        retryResult.IsFailure.Should().BeTrue();
        retryResult.AttemptCount.Should().Be(3);
    }

    [Fact]
    public async Task WithRetry_TerminalError_DoesNotRetry()
    {
        int calls = 0;
        var retryResult = await ((Func<Task<Result<string>>>)(() =>
        {
            calls++;
            return Task.FromResult(Result<string>.Failure(AiError.ContentFiltered()));
        })).WithRetry(maxAttempts: 3, initialDelay: TimeSpan.Zero, useJitter: false);

        retryResult.IsFailure.Should().BeTrue();
        calls.Should().Be(1, "terminal errors must not be retried");
    }

    [Fact]
    public async Task ImplicitConversion_ToResult_Works()
    {
        RetryResult<string> retryResult = await Task.FromResult(Result<string>.Success("hello"))
            .WithRetry(maxAttempts: 1, initialDelay: TimeSpan.Zero, useJitter: false);

        Result<string> result = retryResult; // implicit conversion
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hello");
    }

    [Fact]
    public async Task BindWithRetry_PropagatesUpstreamFailure_WithoutCalling()
    {
        int calls = 0;
        var upstream = Task.FromResult(Result<string>.Failure(Error.Create("upstream failed")));

        var retryResult = await upstream.BindWithRetry(
            _ => { calls++; return Task.FromResult(Result<int>.Success(42)); },
            attempts: 3, initialDelay: TimeSpan.Zero, useJitter: false);

        retryResult.IsFailure.Should().BeTrue();
        calls.Should().Be(0, "operation should not be called when upstream fails");
    }

    [Fact]
    public async Task WithRetry_MaxAttempts1_NeverRetries()
    {
        int calls = 0;
        var retryResult = await ((Func<Task<Result<string>>>)(() =>
        {
            calls++;
            return Task.FromResult(Result<string>.Failure(AiError.RateLimit()));
        })).WithRetry(maxAttempts: 1, initialDelay: TimeSpan.Zero, useJitter: false);

        calls.Should().Be(1);
        retryResult.IsFailure.Should().BeTrue();
    }
}
