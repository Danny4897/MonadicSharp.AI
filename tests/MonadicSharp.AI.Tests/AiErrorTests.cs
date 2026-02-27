using FluentAssertions;
using MonadicSharp.AI.Errors;

namespace MonadicSharp.AI.Tests;

public class AiErrorTests
{
    [Fact]
    public void RateLimit_ShouldHaveCorrectCode()
    {
        var error = AiError.RateLimit();
        error.Code.Should().Be(AiError.RateLimitCode);
        error.Message.Should().Contain("rate limit");
    }

    [Fact]
    public void RateLimit_WithRetryAfter_ShouldIncludeMetadata()
    {
        var error = AiError.RateLimit(retryAfter: TimeSpan.FromSeconds(30));
        error.Metadata["RetryAfterSeconds"].Should().Be(30);
    }

    [Fact]
    public void ModelTimeout_WithModel_ShouldIncludeModelName()
    {
        var error = AiError.ModelTimeout(model: "claude-3-5-sonnet");
        error.Message.Should().Contain("claude-3-5-sonnet");
        error.Code.Should().Be(AiError.ModelTimeoutCode);
    }

    [Fact]
    public void InvalidStructuredOutput_ShouldHaveValidationType()
    {
        var error = AiError.InvalidStructuredOutput("ProductInfo");
        error.Type.Should().Be(ErrorType.Validation);
        error.Message.Should().Contain("ProductInfo");
    }

    [Fact]
    public void InvalidStructuredOutput_TruncatesLongRawOutput()
    {
        var longOutput = new string('x', 600);
        var error = AiError.InvalidStructuredOutput("T", longOutput);
        var stored = error.Metadata["RawOutput"].ToString()!;
        stored.Length.Should().BeLessOrEqualTo(504); // 500 chars + "…"
    }

    [Fact]
    public void ContentFiltered_ShouldHaveForbiddenType()
    {
        var error = AiError.ContentFiltered();
        error.Type.Should().Be(ErrorType.Forbidden);
        error.Code.Should().Be(AiError.ContentFilteredCode);
    }

    [Fact]
    public void AgentStepFailed_ShouldWrapInnerError()
    {
        var inner = AiError.ModelTimeout();
        var error = AiError.AgentStepFailed("Retrieve", inner);

        error.Code.Should().Be(AiError.AgentStepFailedCode);
        error.InnerError.Should().NotBeNull();
        error.InnerError!.Code.Should().Be(AiError.ModelTimeoutCode);
    }

    [Theory]
    [InlineData("AI_RATE_LIMIT",        true)]
    [InlineData("AI_MODEL_TIMEOUT",     true)]
    [InlineData("AI_MODEL_UNAVAILABLE", true)]
    [InlineData("AI_CIRCUIT_OPEN",      true)]
    [InlineData("AI_CONTENT_FILTERED",  false)]
    [InlineData("AI_TOKEN_LIMIT_EXCEEDED", false)]
    [InlineData("AI_INVALID_STRUCTURED_OUTPUT", false)]
    public void IsRetriable_ShouldMatchExpected(string code, bool expected)
    {
        var error = Error.Create("test", code);
        AiError.IsRetriable(error).Should().Be(expected);
        AiError.IsTerminal(error).Should().Be(!expected);
    }
}
