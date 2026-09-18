using System.Text.Json.Serialization;

namespace TypeSafe.Ai;

/// <summary>Token usage reported by a system-one response.</summary>
public sealed class Usage
{
    /// <summary>Gets the input token count.</summary>
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; init; }

    /// <summary>Gets the output token count.</summary>
    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; init; }
}

/// <summary>Typed answers and metadata returned by system-one.</summary>
public sealed class SystemOneResult
{
    /// <summary>Gets the model used to produce the answers.</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>Gets answers keyed by the caller's question names.</summary>
    public IReadOnlyDictionary<string, Answer> Answers { get; init; } =
        new Dictionary<string, Answer>(StringComparer.Ordinal);

    /// <summary>Gets token usage.</summary>
    public Usage Usage { get; init; } = new();
}
