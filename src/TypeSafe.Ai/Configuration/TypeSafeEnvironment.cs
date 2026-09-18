namespace TypeSafe.Ai;

/// <summary>Names and resolution helpers for TypeSafe environment configuration.</summary>
public static class TypeSafeEnvironment
{
    /// <summary>The API key environment variable.</summary>
    public const string ApiKey = "TYPESAFE_API_KEY";

    /// <summary>The API base URL environment variable.</summary>
    public const string BaseUrl = "TYPESAFE_BASE_URL";

    /// <summary>The default model environment variable.</summary>
    public const string DefaultModel = "TYPESAFE_DEFAULT_MODEL";

    /// <summary>The log-level environment variable.</summary>
    public const string LogLevel = "TYPESAFE_LOG_LEVEL";

    /// <summary>Reads and trims an environment value, treating blank values as missing.</summary>
    public static string? Read(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Environment.GetEnvironmentVariable(name)?.Trim() is { Length: > 0 } value
            ? value
            : null;
    }

    /// <summary>Uses an explicit value when present, otherwise reads the named environment value.</summary>
    public static string? FromCodeOrEnvironment(string? explicitValue, string environmentName) =>
        explicitValue ?? Read(environmentName);
}
