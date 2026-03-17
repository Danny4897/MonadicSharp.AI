using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using MonadicSharp.AI.Errors;

namespace MonadicSharp.AI.Recovery;

/// <summary>
/// Self-healing extension methods for AI pipelines.
/// Intercepts <see cref="AiError.InvalidOutputCode"/> failures and sends a repair prompt
/// back to the same <see cref="IChatClient"/>, attempting to fix malformed structured output.
/// </summary>
/// <remarks>
/// This is the Amber-track implementation specialised for AI structured output errors.
/// For generic railway recovery, install <c>MonadicSharp.Recovery</c> and use
/// <c>RescueAsync</c> / <c>StartFixBranchAsync</c> with <see cref="AiRecoveryPredicates"/>.
/// </remarks>
public static class AiSelfHealingExtensions
{
    private static readonly JsonSerializerOptions _jsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    // ── WithSelfHealingAsync ─────────────────────────────────────────────────

    /// <summary>
    /// Wraps an LLM call + JSON parse in a self-healing loop.
    /// On <see cref="AiError.InvalidOutputCode"/>, sends a repair prompt to <paramref name="client"/>
    /// up to <paramref name="maxRepairAttempts"/> times before propagating failure.
    /// </summary>
    /// <typeparam name="T">The strongly-typed schema the LLM should produce as JSON.</typeparam>
    /// <param name="rawResponseTask">
    ///   A pipeline that produces a raw LLM string response.
    ///   Typically the result of calling the LLM and extracting the text content.
    /// </param>
    /// <param name="client">The <see cref="IChatClient"/> to use for repair prompts.</param>
    /// <param name="originalMessages">
    ///   The original conversation that produced the malformed response,
    ///   used as context in the repair prompt.
    /// </param>
    /// <param name="maxRepairAttempts">Max repair cycles (default 2).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <example>
    /// <code>
    /// var messages = new[] { new ChatMessage(ChatRole.User, "Analyze this product...") };
    ///
    /// var result = await client.CompleteAsync(messages, ct: ct)
    ///     .MapAsync(r => r.Message.Text ?? string.Empty)
    ///     .WithSelfHealingAsync&lt;ProductAnalysis&gt;(client, messages);
    ///
    /// result.Match(
    ///     onSuccess: analysis => ...,
    ///     onFailure: error    => ...);
    /// </code>
    /// </example>
    public static async Task<Result<T>> WithSelfHealingAsync<T>(
        this Task<Result<string>>    rawResponseTask,
        IChatClient                  client,
        IEnumerable<ChatMessage>     originalMessages,
        int                          maxRepairAttempts = 2,
        CancellationToken            ct               = default)
    {
        var rawResult = await rawResponseTask.ConfigureAwait(false);

        if (rawResult.IsFailure)
            return Result<T>.Failure(rawResult.Error!);

        // Attempt 0: parse the original response
        var parsed = TryParseJson<T>(rawResult.Value);
        if (parsed.IsSuccess)
            return parsed;

        // Enter Amber track — only for invalid structured output errors
        if (!parsed.Error!.HasCode(AiError.InvalidOutputCode))
            return parsed;

        var originalError  = parsed.Error;
        var previousRaw    = rawResult.Value;
        var schemaHint     = BuildSchemaHint<T>();

        for (int attempt = 1; attempt <= maxRepairAttempts; attempt++)
        {
            var repairMessages = BuildRepairMessages(
                originalMessages, previousRaw, originalError.Message, schemaHint, attempt);

            Result<T> repaired;

            try
            {
                var repairResponse = await client
                    .GetResponseAsync(repairMessages, cancellationToken: ct)
                    .ConfigureAwait(false);

                previousRaw = repairResponse.Messages.LastOrDefault()?.Text?.Trim() ?? string.Empty;
                repaired    = TryParseJson<T>(previousRaw);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Repair call itself failed — stop, propagate original parse error
                _ = ex;
                return Result<T>.Failure(originalError);
            }

            if (repaired.IsSuccess)
                return repaired; // ✅ Amber → Green
        }

        // All repair attempts failed — Red track with original parse error
        return Result<T>.Failure(originalError);
    }

    // ── Parsing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to deserialise <paramref name="raw"/> as <typeparamref name="T"/>.
    /// Strips common LLM noise (markdown fences) before parsing.
    /// On failure, returns <see cref="AiError.InvalidStructuredOutput"/>.
    /// </summary>
    public static Result<T> TryParseJson<T>(string raw)
    {
        var json = StripMarkdownFences(raw);

        if (string.IsNullOrWhiteSpace(json))
            return Result<T>.Failure(
                AiError.InvalidStructuredOutput(typeof(T).Name, raw));

        try
        {
            var value = JsonSerializer.Deserialize<T>(json, _jsonOptions);
            return value is not null
                ? Result<T>.Success(value)
                : Result<T>.Failure(AiError.InvalidStructuredOutput(typeof(T).Name, json));
        }
        catch (JsonException)
        {
            return Result<T>.Failure(AiError.InvalidStructuredOutput(typeof(T).Name, raw));
        }
    }

    // ── Prompt helpers ────────────────────────────────────────────────────────

    private static IList<ChatMessage> BuildRepairMessages(
        IEnumerable<ChatMessage> originalMessages,
        string                   malformedJson,
        string                   parseError,
        string                   schemaHint,
        int                      attempt)
    {
        var messages = new List<ChatMessage>(originalMessages)
        {
            new(ChatRole.System,
                "You are a JSON repair assistant. Return ONLY the corrected JSON. " +
                "No markdown fences, no explanation, no extra text."),

            new(ChatRole.User, BuildRepairPrompt(malformedJson, parseError, schemaHint, attempt)),
        };
        return messages;
    }

    private static string BuildRepairPrompt(
        string malformedJson,
        string parseError,
        string schemaHint,
        int    attempt)
    {
        var sb = new StringBuilder();
        sb.AppendLine(FormattableString.Invariant($"[Repair attempt #{attempt}]"));
        sb.AppendLine();
        sb.AppendLine("This JSON failed to parse:");
        sb.AppendLine(malformedJson);
        sb.AppendLine();
        sb.Append("Error: ").AppendLine(parseError);

        if (!string.IsNullOrWhiteSpace(schemaHint))
        {
            sb.AppendLine();
            sb.AppendLine("Expected structure:");
            sb.AppendLine(schemaHint);
        }

        sb.AppendLine();
        sb.AppendLine("Return ONLY the corrected JSON.");
        return sb.ToString();
    }

    private static string BuildSchemaHint<T>()
    {
        var props = typeof(T).GetProperties(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        if (props.Length == 0) return string.Empty;

        var lines = props.Select(p => FormattableString.Invariant($"  \"{p.Name}\": <{p.PropertyType.Name}>"));
        return FormattableString.Invariant($"{{\n{string.Join(",\n", lines)}\n}}");
    }

    private static string StripMarkdownFences(string text)
    {
        if (!text.Contains("```")) return text.Trim();
        var start = text.IndexOf('{');
        var end   = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text.Trim();
    }
}
