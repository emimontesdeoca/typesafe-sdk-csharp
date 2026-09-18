using System.Text.Json;
using System.Text.Json.Nodes;

namespace TypeSafe.Ai;

/// <summary>The permitted top-level shapes for state and question descriptions.</summary>
public enum EntryValueKind
{
    /// <summary>The JSON null value.</summary>
    Null,
    /// <summary>A JSON string.</summary>
    StringValue,
    /// <summary>A JSON object.</summary>
    ObjectValue,
    /// <summary>A JSON array.</summary>
    ArrayValue,
}

/// <summary>
/// A JSON value accepted at the TypeSafe API boundary. Top-level numbers and booleans are
/// intentionally excluded; they remain valid inside objects and arrays.
/// </summary>
public sealed class EntryValue : IEquatable<EntryValue>
{
    private readonly JsonNode? _node;

    private EntryValue(JsonNode? node, EntryValueKind kind)
    {
        _node = node;
        Kind = kind;
    }

    /// <summary>Gets the shape of this value.</summary>
    public EntryValueKind Kind { get; }

    /// <summary>Gets a reusable JSON null value.</summary>
    public static EntryValue Null { get; } = new(null, EntryValueKind.Null);

    /// <summary>Creates a string entry value.</summary>
    public static EntryValue FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(JsonValue.Create(value), EntryValueKind.StringValue);
    }

    /// <summary>Creates an object entry value from a JSON object.</summary>
    public static EntryValue FromObject(JsonObject value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(value.DeepClone(), EntryValueKind.ObjectValue);
    }

    /// <summary>Creates an array entry value from a JSON array.</summary>
    public static EntryValue FromArray(JsonArray value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(value.DeepClone(), EntryValueKind.ArrayValue);
    }

    /// <summary>Parses and validates one top-level entry value.</summary>
    public static EntryValue Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using JsonDocument document = JsonDocument.Parse(json);
        return FromJsonElement(document.RootElement);
    }

    internal static EntryValue FromJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => Null,
            JsonValueKind.String => FromString(element.GetString()!),
            JsonValueKind.Object => FromObject(JsonNode.Parse(element.GetRawText())!.AsObject()),
            JsonValueKind.Array => FromArray(JsonNode.Parse(element.GetRawText())!.AsArray()),
            _ => throw new TypeSafeException(
                "Entry values must be strings, objects, arrays, or null at the top level."),
        };
    }

    internal JsonNode? CloneNode() => _node?.DeepClone();

    internal void WriteTo(Utf8JsonWriter writer, JsonSerializerOptions options)
    {
        if (_node is null)
        {
            writer.WriteNullValue();
            return;
        }

        _node.WriteTo(writer, options);
    }

    /// <inheritdoc />
    public override string ToString() => _node?.ToJsonString() ?? "null";

    /// <inheritdoc />
    public bool Equals(EntryValue? other)
    {
        return other is not null && Kind == other.Kind &&
            string.Equals(ToString(), other.ToString(), StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is EntryValue other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, ToString());

    /// <summary>Converts a string to an entry value.</summary>
    public static implicit operator EntryValue(string value) => FromString(value);

    /// <summary>Converts a JSON object to an entry value.</summary>
    public static implicit operator EntryValue(JsonObject value) => FromObject(value);

    /// <summary>Converts a JSON array to an entry value.</summary>
    public static implicit operator EntryValue(JsonArray value) => FromArray(value);
}
