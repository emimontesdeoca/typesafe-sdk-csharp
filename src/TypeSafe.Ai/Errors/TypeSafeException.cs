namespace TypeSafe.Ai;

/// <summary>Base exception for SDK configuration, validation, and protocol errors.</summary>
public class TypeSafeException : Exception
{
    /// <summary>Initializes an exception with a diagnostic message.</summary>
    public TypeSafeException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes an exception with a diagnostic message and cause.</summary>
    public TypeSafeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
