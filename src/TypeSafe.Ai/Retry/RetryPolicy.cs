namespace TypeSafe.Ai;

/// <summary>Fully resolved retry behavior for one client or request.</summary>
public sealed class RetryPolicy
{
    /// <summary>Maximum retries after the initial attempt.</summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>Initial exponential backoff in milliseconds.</summary>
    public double BackoffInitialMs { get; init; } = 500;

    /// <summary>Maximum exponential backoff in milliseconds.</summary>
    public double BackoffMaxMs { get; init; } = 5_000;

    /// <summary>Fraction of exponential backoff subtracted as jitter.</summary>
    public double BackoffJitter { get; init; } = 0.25;

    /// <summary>HTTP statuses eligible for retry.</summary>
    public IReadOnlySet<int> HttpStatuses { get; init; } = CreateDefaultStatuses();

    /// <summary>Whether accepted server retry-after values are used.</summary>
    public bool RespectRetryAfter { get; init; } = true;

    /// <summary>Maximum accepted server retry delay in milliseconds.</summary>
    public double MaxRetryAfterMs { get; init; } = 60_000;

    /// <summary>Whether connection failures are retried.</summary>
    public bool ApiConnectionError { get; init; } = true;

    /// <summary>Whether SDK timeout failures are retried.</summary>
    public bool ApiTimeoutError { get; init; } = true;

    /// <summary>Creates a validated copy of the SDK defaults.</summary>
    public static RetryPolicy Default => new();

    internal RetryPolicy Resolve(RetryOptions? overrides)
    {
        RetryOptions value = overrides ?? new RetryOptions();
        var resolved = new RetryPolicy
        {
            MaxRetries = value.MaxRetries ?? MaxRetries,
            BackoffInitialMs = value.BackoffInitialMs ?? BackoffInitialMs,
            BackoffMaxMs = value.BackoffMaxMs ?? BackoffMaxMs,
            BackoffJitter = value.BackoffJitter ?? BackoffJitter,
            HttpStatuses = new HashSet<int>(value.HttpStatuses ?? HttpStatuses),
            RespectRetryAfter = value.RespectRetryAfter ?? RespectRetryAfter,
            MaxRetryAfterMs = value.MaxRetryAfterMs ?? MaxRetryAfterMs,
            ApiConnectionError = value.ApiConnectionError ?? ApiConnectionError,
            ApiTimeoutError = value.ApiTimeoutError ?? ApiTimeoutError,
        };
        Validate(resolved, "retry");
        return resolved;
    }

    internal static void Validate(RetryPolicy policy, string name)
    {
        if (policy.MaxRetries < 0)
        {
            throw new TypeSafeException($"`{name}.maxRetries` must be a non-negative integer, got {policy.MaxRetries}.");
        }

        ValidateMilliseconds($"{name}.backoffInitialMs", policy.BackoffInitialMs, allowZero: true);
        ValidateMilliseconds($"{name}.backoffMaxMs", policy.BackoffMaxMs, allowZero: true);
        if (!double.IsFinite(policy.BackoffJitter) || policy.BackoffJitter is < 0 or > 1)
        {
            throw new TypeSafeException(
                $"`{name}.backoffJitter` must be between 0 and 1, got {policy.BackoffJitter.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
        }

        ArgumentNullException.ThrowIfNull(policy.HttpStatuses);
        foreach (int status in policy.HttpStatuses)
        {
            if (status is < 100 or > 999)
            {
                throw new TypeSafeException($"`{name}.httpStatuses` must contain HTTP status codes, got {status}.");
            }
        }

        ValidateMilliseconds($"{name}.maxRetryAfterMs", policy.MaxRetryAfterMs, allowZero: true);
    }

    private static void ValidateMilliseconds(string name, double value, bool allowZero)
    {
        bool valid = double.IsFinite(value) && (allowZero ? value >= 0 : value > 0);
        if (!valid)
        {
            string requirement = allowZero ? "non-negative" : "positive";
            throw new TypeSafeException(
                $"`{name}` must be a {requirement} number of milliseconds, got {value.ToString(System.Globalization.CultureInfo.InvariantCulture)}.");
        }
    }

    private static HashSet<int> CreateDefaultStatuses()
    {
        var statuses = new HashSet<int> { 408, 429 };
        for (int status = 500; status <= 599; status++)
        {
            statuses.Add(status);
        }

        return statuses;
    }
}

/// <summary>Optional per-call retry overrides.</summary>
public sealed class RetryOptions
{
    /// <summary>Overrides the maximum retry count.</summary>
    public int? MaxRetries { get; init; }

    /// <summary>Overrides the initial backoff.</summary>
    public double? BackoffInitialMs { get; init; }

    /// <summary>Overrides the maximum backoff.</summary>
    public double? BackoffMaxMs { get; init; }

    /// <summary>Overrides the subtractive jitter fraction.</summary>
    public double? BackoffJitter { get; init; }

    /// <summary>Overrides the retryable HTTP status set.</summary>
    public IReadOnlySet<int>? HttpStatuses { get; init; }

    /// <summary>Overrides retry-after handling.</summary>
    public bool? RespectRetryAfter { get; init; }

    /// <summary>Overrides the retry-after ceiling.</summary>
    public double? MaxRetryAfterMs { get; init; }

    /// <summary>Overrides connection retry behavior.</summary>
    public bool? ApiConnectionError { get; init; }

    /// <summary>Overrides timeout retry behavior.</summary>
    public bool? ApiTimeoutError { get; init; }

    /// <summary>Converts a complete policy to per-call overrides.</summary>
    public static implicit operator RetryOptions(RetryPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new RetryOptions
        {
            MaxRetries = policy.MaxRetries,
            BackoffInitialMs = policy.BackoffInitialMs,
            BackoffMaxMs = policy.BackoffMaxMs,
            BackoffJitter = policy.BackoffJitter,
            HttpStatuses = policy.HttpStatuses,
            RespectRetryAfter = policy.RespectRetryAfter,
            MaxRetryAfterMs = policy.MaxRetryAfterMs,
            ApiConnectionError = policy.ApiConnectionError,
            ApiTimeoutError = policy.ApiTimeoutError,
        };
    }
}
