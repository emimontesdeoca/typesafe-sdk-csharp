namespace TypeSafe.Ai;

/// <summary>Logger sink used by the TypeSafe SDK.</summary>
public interface ITypeSafeLogger
{
    /// <summary>Writes a debug message.</summary>
    void Debug(string message, params object?[] args);

    /// <summary>Writes an informational message.</summary>
    void Info(string message, params object?[] args);

    /// <summary>Writes a warning message.</summary>
    void Warn(string message, params object?[] args);

    /// <summary>Writes an error message.</summary>
    void LogError(string message, params object?[] args);
}

/// <summary>Default console logger with the SDK prefix.</summary>
public sealed class ConsoleTypeSafeLogger : ITypeSafeLogger
{
    /// <inheritdoc />
    public void Debug(string message, params object?[] args) => Console.WriteLine(Format(message, args));

    /// <inheritdoc />
    public void Info(string message, params object?[] args) => Console.WriteLine(Format(message, args));

    /// <inheritdoc />
    public void Warn(string message, params object?[] args) => Console.WriteLine(Format(message, args));

    /// <inheritdoc />
    public void LogError(string message, params object?[] args) => Console.Error.WriteLine(Format(message, args));

    private static string Format(string message, object?[] args) =>
        args.Length == 0
            ? $"[typesafe-sdk] {message}"
            : $"[typesafe-sdk] {message} {string.Join(" ", args.Select(argument => argument?.ToString() ?? "null"))}";
}

internal sealed class FilteredTypeSafeLogger : ITypeSafeLogger
{
    private readonly ITypeSafeLogger _sink;
    private readonly TypeSafeLogLevel _level;

    internal FilteredTypeSafeLogger(ITypeSafeLogger sink, TypeSafeLogLevel level)
    {
        _sink = sink;
        _level = level;
    }

    public void Debug(string message, params object?[] args)
    {
        if (Enabled(TypeSafeLogLevel.Debug)) _sink.Debug(message, args);
    }

    public void Info(string message, params object?[] args)
    {
        if (Enabled(TypeSafeLogLevel.Info)) _sink.Info(message, args);
    }

    public void Warn(string message, params object?[] args)
    {
        if (Enabled(TypeSafeLogLevel.Warn)) _sink.Warn(message, args);
    }

    public void LogError(string message, params object?[] args)
    {
        if (Enabled(TypeSafeLogLevel.Error)) _sink.LogError(message, args);
    }

    private bool Enabled(TypeSafeLogLevel level) =>
        (int)level >= (int)_level && _level != TypeSafeLogLevel.Off;
}

/// <summary>Credential and cookie redaction helpers used by debug logging.</summary>
public static class TypeSafeLogRedaction
{
    /// <summary>Copies headers while masking credentials and cookie values.</summary>
    public static IReadOnlyDictionary<string, string> RedactHeaders(
        IReadOnlyDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string value) in headers)
        {
            result[name] = Redact(name, value);
        }

        return result;
    }

    internal static string Redact(string name, string value)
    {
        if (name.Equals("cookie", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("set-cookie", StringComparison.OrdinalIgnoreCase))
        {
            return "***";
        }

        if (name.Equals("authorization", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("proxy-authorization", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("x-api-key", StringComparison.OrdinalIgnoreCase))
        {
            string[] pieces = value.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            string secret = pieces.Length == 2 ? pieces[1] : pieces[0];
            string suffix = secret.Length > 8 ? secret[^4..] : string.Empty;
            return pieces.Length == 2 ? $"{pieces[0]} ***{suffix}" : $"***{suffix}";
        }

        return value;
    }
}
