using System.Text.Json.Serialization;

namespace TypeSafe.Ai;

/// <summary>Metadata for a model available from the TypeSafe API.</summary>
public sealed class ModelCard
{
    /// <summary>Gets the model name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the model description.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets the server-provided release date string.</summary>
    [JsonPropertyName("release_date")]
    public string ReleaseDate { get; init; } = string.Empty;
}
