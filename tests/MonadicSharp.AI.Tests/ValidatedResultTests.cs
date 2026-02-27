using FluentAssertions;
using MonadicSharp.AI.Errors;

namespace MonadicSharp.AI.Tests;

public class ValidatedResultTests
{
    private record Product(string Name, decimal Price, int Stock);

    [Fact]
    public void ParseAs_ValidJson_ReturnsSuccess()
    {
        var json = """{"name":"Widget","price":9.99,"stock":100}""";
        var result = json.ParseAs<Product>().AsResult();

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Widget");
        result.Value.Price.Should().Be(9.99m);
    }

    [Fact]
    public void ParseAs_InvalidJson_ReturnsInvalidOutputError()
    {
        var result = "not json at all".ParseAs<Product>().AsResult();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(AiError.InvalidOutputCode);
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void ParseAs_ValidJson_FailsValidation_ReturnsValidationError()
    {
        var json = """{"name":"Widget","price":-1.0,"stock":0}""";
        var result = json.ParseAs<Product>()
            .Validate(p => p.Price > 0,   "Price must be positive")
            .Validate(p => p.Stock >= 0,  "Stock cannot be negative")
            .AsResult();

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void ParseAs_MultipleFailedRules_CombinesErrors()
    {
        var json = """{"name":"","price":-5.0,"stock":0}""";
        var result = json.ParseAs<Product>()
            .Validate(p => !string.IsNullOrEmpty(p.Name), "Name required")
            .Validate(p => p.Price > 0, "Price must be positive")
            .AsResult();

        result.IsFailure.Should().BeTrue();
        result.Error.SubErrors.Should().HaveCount(2);
    }

    [Fact]
    public void ParseAs_AllRulesPass_ReturnsSuccess()
    {
        var json = """{"name":"Widget","price":9.99,"stock":50}""";
        var result = json.ParseAs<Product>()
            .Validate(p => !string.IsNullOrEmpty(p.Name), "Name required")
            .Validate(p => p.Price > 0, "Price must be positive")
            .Validate(p => p.Stock >= 0, "Stock valid")
            .AsResult();

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ParseAs_FromResultString_PropagatesUpstreamFailure()
    {
        var upstream = Result<string>.Failure(AiError.ModelTimeout());
        var result = upstream.ParseAs<Product>().AsResult();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(AiError.ModelTimeoutCode);
    }

    [Fact]
    public void ParseAs_NullDeserialization_ReturnsInvalidOutputError()
    {
        // JSON "null" deserializes to null for reference types
        var result = "null".ParseAs<Product>().AsResult();
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(AiError.InvalidOutputCode);
    }
}
