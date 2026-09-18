using System.Globalization;

namespace TypeSafe.Ai;

/// <summary>Retry constants and deterministic delay calculations.</summary>
public static class RetryHelpers
{
    /// <summary>The default per-attempt timeout in milliseconds.</summary>
    public const int DefaultTimeoutMs = 10_000;

    /// <summary>Tests whether a response status is in a resolved policy's retry set.</summary>
    public static bool IsRetryableStatus(int status, RetryPolicy? policy = null) =>
        (policy ?? RetryPolicy.Default).HttpStatuses.Contains(status);

    /// <summary>
    /// Parses retry-after-ms first, then Retry-After seconds or an HTTP date, using invariant rules.
    /// </summary>
    public static double? ParseRetryAfter(
        IReadOnlyDictionary<string, string> headers,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(headers);
        if (TryGetHeader(headers, "retry-after-ms", out string? milliseconds) &&
            milliseconds is not null &&
            TryParseNonNegativeFinite(milliseconds, out double parsedMilliseconds))
        {
            return parsedMilliseconds;
        }

        if (!TryGetHeader(headers, "retry-after", out string? raw))
        {
            return null;
        }

        if (raw is not null && TryParseFinite(raw, out double seconds))
        {
            return seconds >= 0 ? seconds * 1000 : null;
        }

        if (DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out DateTimeOffset date))
        {
            double delay = (date.ToUniversalTime() - (now ?? DateTimeOffset.UtcNow)).TotalMilliseconds;
            return Math.Max(0, delay);
        }

        return null;
    }

    /// <summary>Calculates a retry delay in milliseconds for a zero-based retry attempt.</summary>
    public static double CalculateDelayMs(
        int attempt,
        IReadOnlyDictionary<string, string>? headers,
        RetryPolicy policy,
        Func<double>? random = null,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        RetryPolicy.Validate(policy, "retry");
        ArgumentOutOfRangeException.ThrowIfNegative(attempt);

        if (policy.RespectRetryAfter && headers is not null)
        {
            double? retryAfter = ParseRetryAfter(headers, now);
            if (retryAfter is not null && retryAfter <= policy.MaxRetryAfterMs)
            {
                return retryAfter.Value;
            }
        }

        double exponential = Math.Min(
            policy.BackoffInitialMs * Math.Pow(2, attempt),
            policy.BackoffMaxMs);
        double unit = random?.Invoke() ?? Random.Shared.NextDouble();
        if (!double.IsFinite(unit) || unit < 0 || unit > 1)
        {
            throw new TypeSafeException(
                $"Retry random source must return a value from 0 to 1, got {unit.ToString(CultureInfo.InvariantCulture)}.");
        }

        double jittered = exponential * (1 - unit * policy.BackoffJitter);
        return Math.Round(jittered, MidpointRounding.AwayFromZero);
    }

    /// <summary>Waits for a retry delay while observing cancellation.</summary>
    public static Task SleepAsync(double milliseconds, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(milliseconds));
        }

        return Task.Delay(TimeSpan.FromMilliseconds(milliseconds), cancellationToken);
    }

    private static bool TryGetHeader(
        IReadOnlyDictionary<string, string> headers,
        string name,
        out string? value)
    {
        foreach ((string key, string headerValue) in headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = headerValue;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryParseFinite(string value, out double result)
    {
        return double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result) && double.IsFinite(result);
    }

    private static bool TryParseNonNegativeFinite(string value, out double result) =>
        TryParseFinite(value, out result) && result >= 0;
}
