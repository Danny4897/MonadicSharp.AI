using FluentAssertions;
using MonadicSharp.AI.Errors;

namespace MonadicSharp.AI.Tests;

public class AgentResultTests
{
    [Fact]
    public async Task ExecuteAsync_AllStepsSucceed_ReturnsSuccessWithTrace()
    {
        var result = await AgentResult
            .StartTrace("TestPipeline", "input")
            .Step("Step1", v => Task.FromResult(Result<string>.Success(v + "_1")))
            .Step("Step2", v => Task.FromResult(Result<string>.Success(v + "_2")))
            .ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("input_1_2");
        result.Trace.Steps.Should().HaveCount(2);
        result.Trace.Steps[0].Name.Should().Be("Step1");
        result.Trace.Steps[1].Name.Should().Be("Step2");
        result.Trace.Steps.Should().OnlyContain(s => s.Succeeded);
    }

    [Fact]
    public async Task ExecuteAsync_StepFails_StopsAndWrapsError()
    {
        var result = await AgentResult
            .StartTrace("TestPipeline", "input")
            .Step("Step1", v => Task.FromResult(Result<string>.Success(v + "_1")))
            .Step("Step2", _ => Task.FromResult(Result<string>.Failure(AiError.ModelTimeout())))
            .Step("Step3", v => Task.FromResult(Result<string>.Success(v + "_3")))
            .ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(AiError.AgentStepFailedCode);
        result.Error.Metadata["StepName"].Should().Be("Step2");

        result.Trace.Steps.Should().HaveCount(2, "Step3 should not have run");
        result.Trace.Steps[1].Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Trace_AlwaysPopulated_EvenOnFailure()
    {
        var result = await AgentResult
            .StartTrace("FailingPipeline", 0)
            .Step("CountUp",  n => Task.FromResult(Result<int>.Success(n + 1)))
            .Step("FailHere", _ => Task.FromResult(Result<int>.Failure(Error.Create("boom"))))
            .ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.Trace.Steps.Should().HaveCount(2);
        result.Trace.TotalDuration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task Map_PreservesTrace()
    {
        var agentResult = await AgentResult
            .StartTrace("Mapping", "hello")
            .Step("Upper", v => Task.FromResult(Result<string>.Success(v.ToUpper())))
            .ExecuteAsync();

        var mapped = agentResult.Map(s => s.Length);

        mapped.IsSuccess.Should().BeTrue();
        mapped.Value.Should().Be(5);
        mapped.Trace.Steps.Should().HaveCount(1);
    }

    [Fact]
    public async Task ImplicitConversion_ToResult_Works()
    {
        AgentResult<string> agentResult = await AgentResult
            .StartTrace("Simple", "value")
            .ExecuteAsync();

        Result<string> result = agentResult; // implicit
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("value");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyPipeline_ReturnsInitialValue()
    {
        var result = await AgentResult
            .StartTrace("EmptyPipeline", 42)
            .ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        result.Trace.Steps.Should().BeEmpty();
    }
}
