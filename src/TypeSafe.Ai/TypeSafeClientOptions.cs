using System.Text.Json.Serialization;

namespace TypeSafe.Ai;

/// <summary>Configuration for the TypeSafe client.</summary>
public sealed class TypeSafeClientOptions
{
    /// <summary>API key supplied to the client. It is ignored by JSON serialization and never exposed by the client.</summary>
    [JsonIgnore]
    public string? ApiKey { get; init; }

    /// <summary>API base URI. The default is https://api.typesafe.ai.</summary>
    public Uri? BaseUri { get; init; }

    /// <summary>Default model used when a request omits one.</summary>
    public string? DefaultModel { get; init; }

    /// <summary>Configured SDK log level.</summary>
    public TypeSafeLogLevel? LogLevel { get; init; }

    /// <summary>Custom logger sink.</summary>
    [JsonIgnore]
    public ITypeSafeLogger? Logger { get; init; }

    /// <summary>Complete client retry policy.</summary>
    [JsonIgnore]
    public RetryPolicy? Retry { get; init; }

    /// <summary>Per-attempt timeout.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Headers merged into every request.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, string>? DefaultHeaders { get; init; }

    /// <summary>Allows use from browser/WASM runtimes, exposing the API key to page users.</summary>
    public bool AllowBrowser { get; init; }

    /// <summary>An externally managed HttpClient. The client does not dispose it.</summary>
    [JsonIgnore]
    public HttpClient? HttpClient { get; init; }

    /// <summary>A handler used to create an SDK-owned HttpClient.</summary>
    [JsonIgnore]
    public HttpMessageHandler? HttpMessageHandler { get; init; }

    /// <summary>Maximum response body size buffered by the SDK.</summary>
    public long MaxResponseBodyBytes { get; init; } = 10 * 1024 * 1024;
}

/// <summary>Per-call request settings. The cancellation token is supplied to the API method.</summary>
public sealed class RequestOptions
{
    /// <summary>Overrides the per-attempt timeout.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Overrides selected retry settings for this call.</summary>
    public RetryOptions? Retry { get; init; }

    /// <summary>Headers merged over client defaults before protected SDK headers are applied.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
}
