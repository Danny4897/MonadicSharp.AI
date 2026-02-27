// SemanticKernelBridge — extensions that require Microsoft.SemanticKernel
//
// This file is compiled only when the consuming project has a reference to
// Microsoft.SemanticKernel (1.x+). Add the package to your project to use these
// helpers; MonadicSharp.AI itself does NOT depend on SemanticKernel.
//
// Usage in your project:
//   <PackageReference Include="Microsoft.SemanticKernel" Version="1.*" />

#if SEMANTIC_KERNEL

using Microsoft.SemanticKernel;

namespace MonadicSharp.AI.Extensions;

/// <summary>
/// Bridge between MonadicSharp <see cref="Result{T}"/> and Semantic Kernel types.
/// Enables seamless integration without polluting SK pipelines with exception-based control flow.
/// </summary>
public static class SemanticKernelBridge
{
    /// <summary>
    /// Converts a <see cref="FunctionResult"/> to <see cref="Result{String}"/>.
    /// Returns Failure if the result value is null or empty.
    /// </summary>
    public static Result<string> ToResult(this FunctionResult functionResult)
    {
        try
        {
            var value = functionResult.GetValue<string>();
            return string.IsNullOrEmpty(value)
                ? Result<string>.Failure(Error.Create("Semantic Kernel function returned empty result.", "SK_EMPTY_RESULT"))
                : Result<string>.Success(value);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure(Error.FromException(ex, "SK_RESULT_ERROR"));
        }
    }

    /// <summary>
    /// Converts a <see cref="FunctionResult"/> to <see cref="Result{T}"/> by deserializing the value.
    /// </summary>
    public static Result<T> ToResult<T>(this FunctionResult functionResult)
    {
        try
        {
            var value = functionResult.GetValue<T>();
            return value is not null
                ? Result<T>.Success(value)
                : Result<T>.Failure(AiError.InvalidStructuredOutput(typeof(T).Name));
        }
        catch (Exception ex)
        {
            return Result<T>.Failure(Error.FromException(ex, "SK_RESULT_ERROR"));
        }
    }

    /// <summary>
    /// Invokes a Semantic Kernel function and wraps the result in <see cref="Result{String}"/>.
    /// Exceptions from SK are captured as typed errors — no try/catch required at the call site.
    /// </summary>
    public static async Task<Result<string>> InvokeAsResultAsync(
        this Kernel kernel,
        KernelFunction function,
        KernelArguments? arguments = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await kernel.InvokeAsync(function, arguments, cancellationToken);
            return result.ToResult();
        }
        catch (KernelFunctionCanceledException)
        {
            return Result<string>.Failure(Error.Create("SK function was cancelled.", "SK_CANCELLED", ErrorType.Failure));
        }
        catch (Exception ex)
        {
            return Result<string>.Failure(Error.FromException(ex, "SK_INVOKE_ERROR"));
        }
    }

    /// <summary>
    /// Invokes a Semantic Kernel prompt and wraps the response in <see cref="Result{String}"/>.
    /// </summary>
    public static async Task<Result<string>> InvokePromptAsResultAsync(
        this Kernel kernel,
        string prompt,
        KernelArguments? arguments = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await kernel.InvokePromptAsync(prompt, arguments, cancellationToken: cancellationToken);
            return result.ToResult();
        }
        catch (Exception ex)
        {
            return Result<string>.Failure(Error.FromException(ex, "SK_PROMPT_ERROR"));
        }
    }

    /// <summary>
    /// Streams a prompt and returns a <see cref="StreamResult"/> for monadic stream handling.
    /// </summary>
    public static StreamResult StreamPromptAsResult(
        this Kernel kernel,
        string prompt,
        KernelArguments? arguments = null)
    {
        return AsyncTokens(kernel, prompt, arguments).ToStreamResult();
    }

    private static async IAsyncEnumerable<string> AsyncTokens(
        Kernel kernel,
        string prompt,
        KernelArguments? arguments,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in kernel.InvokePromptStreamingAsync<string>(prompt, arguments, cancellationToken: cancellationToken))
        {
            if (chunk is not null)
                yield return chunk;
        }
    }
}

#else

// SemanticKernelBridge is available when you add:
//   <PackageReference Include="Microsoft.SemanticKernel" Version="1.*" />
// and define the SEMANTIC_KERNEL compilation symbol in your project.

#endif
