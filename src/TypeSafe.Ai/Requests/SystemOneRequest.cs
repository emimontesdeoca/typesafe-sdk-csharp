using System.Text.Json.Nodes;

namespace TypeSafe.Ai;

/// <summary>State, questions, and optional fields sent to system-one.</summary>
public sealed class SystemOneRequest
{
    /// <summary>Gets or sets the state to evaluate. JSON null is supported.</summary>
    public EntryValue? State { get; init; }

    /// <summary>Gets or sets the non-empty named question set.</summary>
    public IReadOnlyDictionary<string, Question> Questions { get; init; } =
        new Dictionary<string, Question>(StringComparer.Ordinal);

    private string? _model;

    /// <summary>Gets or sets an optional per-call model override.</summary>
    public string? Model
    {
        get => _model;
        init
        {
            _model = value;
            ModelSpecified = true;
        }
    }

    /// <summary>Gets extension fields forwarded at the top level, including explicit null values.</summary>
    public IReadOnlyDictionary<string, JsonNode?> ExtensionData { get; init; } =
        new Dictionary<string, JsonNode?>(StringComparer.Ordinal);

    internal bool ModelSpecified { get; private set; }

    internal void SetModel(string? value)
    {
        _model = value;
        ModelSpecified = true;
    }
}
