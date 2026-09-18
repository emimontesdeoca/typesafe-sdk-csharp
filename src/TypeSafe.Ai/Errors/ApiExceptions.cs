using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TypeSafe.Ai;

/// <summary>An unsuccessful HTTP response from the TypeSafe API.</summary>
public class ApiException : TypeSafeException
{
    private const int MaxRawBodyInMessage = 200;

    /// <summary>Initializes an API exception with its response metadata.</summary>
    protected ApiException(
        int status,
        object? body,
        IReadOnlyDictionary<string, string> headers,
        bool hasBody = true,
        string? message = null)
        : base(message ?? Describe(status, body, hasBody))
    {
        Status = status;
        Body = body;
        Headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
        HasBody = hasBody;
        RequestId = HeaderValue(Headers, "x-typesafe-request-id");
    }

    /// <summary>HTTP response status code.</summary>
    public int Status { get; }

    /// <summary>Parsed JSON, response text, or null for an empty body.</summary>
    public object? Body { get; }

    /// <summary>HTTP response headers copied from the failed response.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>Whether a response body was present, including an explicit JSON null.</summary>
    public bool HasBody { get; }

    /// <summary>The server request ID, when supplied.</summary>
    public string? RequestId { get; }

    /// <summary>Creates the mapped SDK exception for an HTTP status.</summary>
    public static ApiException FromResponse(
        int status,
        object? body,
        IReadOnlyDictionary<string, string> headers) =>
        FromResponse(status, body, headers, hasBody: body is not null);

    internal static ApiException FromResponse(
        int status,
        object? body,
        IReadOnlyDictionary<string, string> headers,
        bool hasBody)
    {
        return status switch
        {
            400 => new BadRequestException(status, body, headers, hasBody),
            401 => new AuthenticationException(status, body, headers, hasBody),
            403 => new PermissionDeniedException(status, body, headers, hasBody),
            404 => new NotFoundException(status, body, headers, hasBody),
            422 => new UnprocessableEntityException(status, body, headers, hasBody),
            429 => new RateLimitException(status, body, headers, hasBody),
            >= 500 => new InternalServerException(status, body, headers, hasBody),
            _ => new ApiException(status, body, headers, hasBody),
        };
    }

    internal static string? HeaderValue(IReadOnlyDictionary<string, string> headers, string name)
    {
        foreach ((string key, string value) in headers)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    private static string Describe(int status, object? body, bool hasBody)
    {
        string? detail = ExtractMessage(body);
        if (!string.IsNullOrEmpty(detail))
        {
            return $"{status} {detail}";
        }

        if (!hasBody)
        {
            return $"{status} status code (no body)";
        }

        string raw = RawBody(body);
        return $"{status} {(raw.Length > MaxRawBodyInMessage ? raw[..MaxRawBodyInMessage] + "…" : raw)}";
    }

    internal static string? ExtractMessage(object? body)
    {
        if (body is string text)
        {
            return text.Length == 0 ? null : text;
        }

        if (body is not JsonObject root)
        {
            return null;
        }

        if (GetString(root["error"]) is string error)
        {
            return error;
        }

        if (root["error"] is JsonObject errorObject && GetString(errorObject["message"]) is string errorMessage)
        {
            return errorMessage;
        }

        if (GetString(root["message"]) is string message)
        {
            return message;
        }

        if (GetString(root["detail"]) is string detail)
        {
            return detail;
        }

        if (root["detail"] is JsonObject detailObject && GetString(detailObject["message"]) is string detailMessage)
        {
            return detailMessage;
        }

        if (root["detail"] is JsonArray validation)
        {
            var parts = new List<string>();
            foreach (JsonNode? item in validation)
            {
                if (item is not JsonObject entry || GetString(entry["msg"]) is not string itemMessage)
                {
                    continue;
                }

                var locations = new List<string>();
                if (entry["loc"] is JsonArray location)
                {
                    foreach (JsonNode? part in location)
                    {
                        string value = JsonScalarString(part);
                        if (!string.Equals(value, "body", StringComparison.Ordinal))
                        {
                            locations.Add(value);
                        }
                    }
                }

                parts.Add(locations.Count == 0 ? itemMessage : $"{string.Join(".", locations)}: {itemMessage}");
            }

            return parts.Count == 0 ? null : string.Join("; ", parts);
        }

        return null;
    }

    private static string RawBody(object? body)
    {
        return body switch
        {
            null => "null",
            string text => text,
            JsonNode node => node.ToJsonString(),
            _ => JsonSerializer.Serialize(body),
        };
    }

    private static string? GetString(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out string? text))
        {
            return text;
        }

        return null;
    }

    private static string JsonScalarString(JsonNode? node)
    {
        if (GetString(node) is string text)
        {
            return text;
        }

        return node?.ToJsonString() ?? string.Empty;
    }
}

/// <summary>HTTP 400 request error.</summary>
public sealed class BadRequestException : ApiException
{
    internal BadRequestException(int status, object? body, IReadOnlyDictionary<string, string> headers, bool hasBody)
        : base(status, body, headers, hasBody) { }
}

/// <summary>HTTP 401 authentication error.</summary>
public sealed class AuthenticationException : ApiException
{
    internal AuthenticationException(int status, object? body, IReadOnlyDictionary<string, string> headers, bool hasBody)
        : base(status, body, headers, hasBody) { }
}

/// <summary>HTTP 403 permission error.</summary>
public sealed class PermissionDeniedException : ApiException
{
    internal PermissionDeniedException(int status, object? body, IReadOnlyDictionary<string, string> headers, bool hasBody)
        : base(status, body, headers, hasBody) { }
}

/// <summary>HTTP 404 not-found error.</summary>
public sealed class NotFoundException : ApiException
{
    internal NotFoundException(int status, object? body, IReadOnlyDictionary<string, string> headers, bool hasBody)
        : base(status, body, headers, hasBody) { }
}

/// <summary>HTTP 422 validation error.</summary>
public sealed class UnprocessableEntityException : ApiException
{
    internal UnprocessableEntityException(int status, object? body, IReadOnlyDictionary<string, string> headers, bool hasBody)
        : base(status, body, headers, hasBody) { }
}

/// <summary>HTTP 429 rate-limit error.</summary>
public sealed class RateLimitException : ApiException
{
    internal RateLimitException(int status, object? body, IReadOnlyDictionary<string, string> headers, bool hasBody)
        : base(status, body, headers, hasBody)
    {
        RetryAfterMs = RetryHelpers.ParseRetryAfter(Headers);
    }

    /// <summary>Accepted server retry delay in milliseconds, if present.</summary>
    public double? RetryAfterMs { get; }
}

/// <summary>HTTP 5xx server error.</summary>
public sealed class InternalServerException : ApiException
{
    internal InternalServerException(int status, object? body, IReadOnlyDictionary<string, string> headers, bool hasBody)
        : base(status, body, headers, hasBody) { }
}

/// <summary>A request or response-body delivery failure.</summary>
public class ApiConnectionException : TypeSafeException
{
    /// <summary>Initializes a connection exception.</summary>
    public ApiConnectionException(string message = "Connection error.", Exception? innerException = null)
        : base(message, innerException!)
    {
    }
}

/// <summary>A request exceeded its per-attempt timeout.</summary>
public sealed class ApiTimeoutException : ApiConnectionException
{
    /// <summary>Initializes a timeout exception.</summary>
    public ApiTimeoutException(double timeoutMs, Exception? innerException = null)
        : base($"Request timed out after {timeoutMs.ToString(CultureInfo.InvariantCulture)}ms.", innerException)
    {
        TimeoutMs = timeoutMs;
    }

    /// <summary>Configured timeout for the failed attempt in milliseconds.</summary>
    public double TimeoutMs { get; }
}

/// <summary>The caller cancelled a request or pending retry.</summary>
public sealed class ApiUserAbortException : TypeSafeException
{
    /// <summary>Initializes a caller-cancellation exception.</summary>
    public ApiUserAbortException(string message = "Request was aborted.", Exception? innerException = null)
        : base(message, innerException!)
    {
    }
}

internal readonly record struct ParsedErrorBody(object? Value, bool HasBody);

internal static class ErrorBodyParser
{
    internal static ParsedErrorBody Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new ParsedErrorBody(null, false);
        }

        try
        {
            return new ParsedErrorBody(JsonNode.Parse(text), true);
        }
        catch (JsonException)
        {
            return new ParsedErrorBody(text, true);
        }
    }
}
