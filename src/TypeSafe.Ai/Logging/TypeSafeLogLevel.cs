namespace TypeSafe.Ai;

/// <summary>SDK log verbosity, ordered from most verbose to disabled.</summary>
public enum TypeSafeLogLevel
{
    /// <summary>Request headers and bodies.</summary>
    Debug,
    /// <summary>Request summaries.</summary>
    Info,
    /// <summary>Warnings only.</summary>
    Warn,
    /// <summary>Errors only.</summary>
    Error,
    /// <summary>Disable all SDK logging.</summary>
    Off,
}

internal static class TypeSafeLogLevelParser
{
    internal static TypeSafeLogLevel Parse(string value, string source)
    {
        if (Enum.TryParse(value, ignoreCase: true, out TypeSafeLogLevel level) &&
            string.Equals(level.ToString(), value, StringComparison.OrdinalIgnoreCase))
        {
            return level;
        }

        throw new TypeSafeException(
            $"Invalid log level \"{value}\" from {source}. Expected one of: debug, info, warn, error, off.");
    }

    internal static string Format(TypeSafeLogLevel level) => level switch
    {
        TypeSafeLogLevel.Debug => "debug",
        TypeSafeLogLevel.Info => "info",
        TypeSafeLogLevel.Warn => "warn",
        TypeSafeLogLevel.Error => "error",
        TypeSafeLogLevel.Off => "off",
        _ => throw new ArgumentOutOfRangeException(nameof(level)),
    };
}
