namespace TypeSafe.Ai;

internal static class HeaderUtilities
{
    internal static Dictionary<string, string> Merge(
        params IEnumerable<KeyValuePair<string, string?>>[] sources)
    {
        var values = new Dictionary<string, (string Name, string Value)>(StringComparer.OrdinalIgnoreCase);
        foreach (IEnumerable<KeyValuePair<string, string?>> source in sources)
        {
            foreach ((string name, string? value) in source)
            {
                ArgumentException.ThrowIfNullOrEmpty(name);
                string normalized = name.ToLowerInvariant();
                if (value is null)
                {
                    values.Remove(normalized);
                }
                else
                {
                    values[normalized] = (name, value);
                }
            }
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string value) in values.Values)
        {
            result[name] = value;
        }

        return result;
    }

    internal static IEnumerable<KeyValuePair<string, string?>> AsNullable(
        IReadOnlyDictionary<string, string>? values)
    {
        return values is null
            ? Enumerable.Empty<KeyValuePair<string, string?>>()
            : values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value));
    }
}
