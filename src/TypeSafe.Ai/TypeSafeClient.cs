using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TypeSafe.Ai;

/// <summary>Client for the TypeSafe AI API.</summary>
public sealed class TypeSafeClient : IDisposable
{
    /// <summary>The default API base URI.</summary>
    public static Uri DefaultBaseUri { get; } = new("https://api.typesafe.ai");

    /// <summary>The default model name.</summary>
    public const string DefaultModelName = "jev-latest";

    private readonly string _apiKey;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly long _maxResponseBodyBytes;
    private long _requestCount;
    private int _disposed;

    /// <summary>Gets the normalized API base URI.</summary>
    public Uri BaseUri { get; }

    /// <summary>Gets the model used when a request omits one.</summary>
    public string DefaultModel { get; }

    /// <summary>Gets the configured log level.</summary>
    public TypeSafeLogLevel LogLevel { get; }

    /// <summary>Gets the fully resolved client retry policy.</summary>
    public RetryPolicy Retry { get; }

    /// <summary>Gets the per-attempt timeout.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>Gets the models resource.</summary>
    public ModelsResource Models { get; }

    /// <summary>Gets the logger filtered to the configured level.</summary>
    public ITypeSafeLogger Logger { get; }

    /// <summary>Creates a client using options and an SDK-owned default HttpClient.</summary>
    public TypeSafeClient(TypeSafeClientOptions? options = null)
        : this(options ?? new TypeSafeClientOptions(), null, null)
    {
    }

    /// <summary>Creates a client using an externally managed HttpClient.</summary>
    public TypeSafeClient(HttpClient httpClient, TypeSafeClientOptions? options = null)
        : this(options ?? new TypeSafeClientOptions(), httpClient, null)
    {
    }

    /// <summary>Creates a client using an SDK-owned HttpClient built from a handler.</summary>
    public TypeSafeClient(HttpMessageHandler handler, TypeSafeClientOptions? options = null)
        : this(options ?? new TypeSafeClientOptions(), null, handler)
    {
    }

    private TypeSafeClient(
        TypeSafeClientOptions options,
        HttpClient? explicitHttpClient,
        HttpMessageHandler? explicitHandler)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (RuntimeDescription.IsBrowser && !options.AllowBrowser)
        {
            throw new TypeSafeException(
                "TypeSafeClient is running in a browser, which would expose your API key to page users. " +
                "Call the API from a server instead, or set AllowBrowser to true if you understand the risk.");
        }

        if (explicitHttpClient is not null && (options.HttpClient is not null || options.HttpMessageHandler is not null))
        {
            throw new ArgumentException("Provide an HttpClient either as a constructor argument or in options, not both.");
        }

        if (explicitHandler is not null && (options.HttpClient is not null || options.HttpMessageHandler is not null))
        {
            throw new ArgumentException("Provide an HttpMessageHandler either as a constructor argument or in options, not both.");
        }

        _apiKey = options.ApiKey ?? TypeSafeEnvironment.Read(TypeSafeEnvironment.ApiKey)
            ?? throw new TypeSafeException(
                $"No API key was provided. Pass `ApiKey` to the TypeSafeClient options or set the {TypeSafeEnvironment.ApiKey} environment variable.");

        string baseUrl = options.BaseUri?.ToString()
            ?? TypeSafeEnvironment.Read(TypeSafeEnvironment.BaseUrl)
            ?? DefaultBaseUri.ToString();
        baseUrl = baseUrl.TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new TypeSafeException($"Invalid base URL: {baseUrl}.");
        }

        BaseUri = baseUri;
        DefaultModel = options.DefaultModel
            ?? TypeSafeEnvironment.Read(TypeSafeEnvironment.DefaultModel)
            ?? DefaultModelName;

        string? environmentLevel = TypeSafeEnvironment.Read(TypeSafeEnvironment.LogLevel);
        LogLevel = options.LogLevel ??
            (environmentLevel is null
                ? TypeSafeLogLevel.Warn
                : TypeSafeLogLevelParser.Parse(environmentLevel, TypeSafeEnvironment.LogLevel));
        Logger = new FilteredTypeSafeLogger(options.Logger ?? new ConsoleTypeSafeLogger(), LogLevel);

        RetryPolicy basePolicy = options.Retry ?? RetryPolicy.Default;
        Retry = basePolicy.Resolve(null);
        Timeout = ValidateTimeout(options.Timeout ?? TimeSpan.FromMilliseconds(RetryHelpers.DefaultTimeoutMs), "timeout");
        if (options.MaxResponseBodyBytes <= 0)
        {
            throw new TypeSafeException("`maxResponseBodyBytes` must be positive.");
        }

        _maxResponseBodyBytes = options.MaxResponseBodyBytes;
        DefaultHeaders = new Dictionary<string, string>(
            options.DefaultHeaders ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);

        if (explicitHttpClient is not null)
        {
            _httpClient = ConfigureHttpClient(explicitHttpClient);
            _ownsHttpClient = false;
        }
        else if (options.HttpClient is not null)
        {
            _httpClient = ConfigureHttpClient(options.HttpClient);
            _ownsHttpClient = false;
        }
        else
        {
            HttpMessageHandler handler = explicitHandler ?? options.HttpMessageHandler ?? CreateDefaultHandler();
            _httpClient = new HttpClient(handler, disposeHandler: true);
            _httpClient.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
            _ownsHttpClient = true;
        }

        Models = new ModelsResource(this);
    }

    /// <summary>Gets immutable client default headers with case-insensitive lookup.</summary>
    public IReadOnlyDictionary<string, string> DefaultHeaders { get; }

    /// <summary>Answers named questions about text or structured state.</summary>
    public ApiPromise<SystemOneResult> SystemOneAsync(
        SystemOneRequest request,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Questions.Validate(request.Questions);
        var payload = new SystemOneRequest
        {
            State = request.State,
            Questions = request.Questions,
            Model = request.Model ?? DefaultModel,
            ExtensionData = request.ExtensionData,
        };

        return RequestAsync(
            HttpMethod.Post,
            "/v1/systemone",
            hasBody: true,
            payload,
            options,
            ParseSystemOneAsync,
            cancellationToken);
    }

    internal ApiPromise<T> RequestAsync<T>(
        HttpMethod method,
        string path,
        bool hasBody,
        object? body,
        RequestOptions? options,
        Func<BufferedResponse, Task<T>> parser,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        ArgumentNullException.ThrowIfNull(parser);
        RequestOptions requestOptions = options ?? new RequestOptions();
        TimeSpan timeout = ValidateTimeout(requestOptions.Timeout ?? Timeout, "timeout");
        RetryPolicy retry = Retry.Resolve(requestOptions.Retry);
        Dictionary<string, string> userHeaders = HeaderUtilities.Merge(
            HeaderUtilities.AsNullable(DefaultHeaders),
            HeaderUtilities.AsNullable(requestOptions.Headers));
        string? bodyJson = hasBody
            ? JsonSerializer.Serialize(body, Json.TypeSafeJson.Options)
            : null;
        long requestNumber = Interlocked.Increment(ref _requestCount);
        string tag = $"#{requestNumber} {method.Method} {path}";
        return new ApiPromise<T>(
            FetchWithRetriesAsync(tag, method, path, userHeaders, bodyJson, body, timeout, retry, hasBody, cancellationToken),
            parser);
    }

    private async Task<BufferedResponse> FetchWithRetriesAsync(
        string tag,
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, string> userHeaders,
        string? body,
        object? logBody,
        TimeSpan timeout,
        RetryPolicy retry,
        bool hasBody,
        CancellationToken cancellationToken)
    {
        string url = $"{BaseUri.AbsoluteUri.TrimEnd('/')}{path}";
        Dictionary<string, string> protectedHeaders = HeaderUtilities.Merge(
            HeaderUtilities.AsNullable(userHeaders),
            new Dictionary<string, string?>
            {
                ["Authorization"] = $"Bearer {_apiKey}",
                ["Accept"] = "application/json",
                ["User-Agent"] = $"typesafe-sdk/{TypeSafeVersion.SdkVersion}",
                ["X-TypeSafe-SDK"] = $"typesafe-sdk/{TypeSafeVersion.SdkVersion}",
                ["X-TypeSafe-Runtime"] = RuntimeDescription.Current,
                ["Content-Type"] = hasBody ? "application/json" : null,
                ["X-TypeSafe-Retry-Count"] = null,
            });

        for (int attempt = 0; ; attempt++)
        {
            int retriesLeft = retry.MaxRetries - attempt;
            Dictionary<string, string> attemptHeaders = HeaderUtilities.Merge(
                HeaderUtilities.AsNullable(protectedHeaders),
                new Dictionary<string, string?>
                {
                    ["X-TypeSafe-Retry-Count"] = attempt == 0
                        ? null
                        : attempt.ToString(CultureInfo.InvariantCulture),
                });
            Logger.Debug(
                $"{tag} -> {url}",
                new { headers = TypeSafeLogRedaction.RedactHeaders(attemptHeaders), body = SanitizeForLog(logBody) });

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                BufferedResponse response = await AttemptAsync(
                    tag,
                    new Uri(url),
                    method,
                    attemptHeaders,
                    body,
                    timeout,
                    hasBody,
                    cancellationToken).ConfigureAwait(false);
                string requestId = response.RequestId is null ? string.Empty : $" (request {response.RequestId})";
                Logger.Info($"{tag} <- {response.Status} in {stopwatch.ElapsedMilliseconds}ms{requestId}");
                if (response.IsSuccessStatusCode)
                {
                    return response;
                }

                ParsedErrorBody parsedBody = response.ParseBody();
                Logger.Debug($"{tag} <- error body", SanitizeForLog(parsedBody.Value));
                ApiException error = ApiException.FromResponse(
                    response.Status,
                    parsedBody.Value,
                    response.Headers,
                    parsedBody.HasBody);
                if (retriesLeft <= 0 || !RetryHelpers.IsRetryableStatus(response.Status, retry))
                {
                    throw error;
                }

                await BackOffAsync(
                    tag,
                    attempt,
                    retriesLeft,
                    response.Status.ToString(CultureInfo.InvariantCulture),
                    response.Headers,
                    retry,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (ApiUserAbortException)
            {
                throw;
            }
            catch (ApiException)
            {
                throw;
            }
            catch (ApiConnectionException exception)
            {
                if (retriesLeft <= 0 || !IsRetryableConnection(exception, retry))
                {
                    throw;
                }

                await BackOffAsync(
                    tag,
                    attempt,
                    retriesLeft,
                    exception.Message,
                    null,
                    retry,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<BufferedResponse> AttemptAsync(
        string tag,
        Uri url,
        HttpMethod method,
        IReadOnlyDictionary<string, string> headers,
        string? body,
        TimeSpan timeout,
        bool hasBody,
        CancellationToken callerCancellation)
    {
        if (callerCancellation.IsCancellationRequested)
        {
            throw new ApiUserAbortException(innerException: new OperationCanceledException(callerCancellation));
        }

        using var timeoutSource = new CancellationTokenSource();
        timeoutSource.CancelAfter(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            callerCancellation,
            timeoutSource.Token);
        TaskCompletionSource<bool> callerSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = callerCancellation.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
            callerSignal);
        HttpRequestMessage request = CreateRequest(url, method, headers, body, hasBody);
        var attemptState = new AttemptState(request);
        Task<BufferedResponse> operation = SendAndBufferAsync(attemptState, linkedSource.Token);
        using var timeoutSignalSource = new CancellationTokenSource();
        Task timeoutSignal = Task.Delay(timeout, timeoutSignalSource.Token);
        Task completed = await Task.WhenAny(operation, timeoutSignal, callerSignal.Task).ConfigureAwait(false);

        if (operation.IsCompleted)
        {
            timeoutSignalSource.Cancel();
            try
            {
                return await operation.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                throw ClassifyAttemptException(tag, exception, timeout, timeoutSource, callerCancellation);
            }
        }

        linkedSource.Cancel();
        attemptState.Abort();
        timeoutSignalSource.Cancel();
        if (callerSignal.Task.IsCompleted || callerCancellation.IsCancellationRequested)
        {
            _ = ObserveAbandonedOperationAsync(operation);
            throw new ApiUserAbortException(innerException: new OperationCanceledException(callerCancellation));
        }

        if (completed == timeoutSignal || timeoutSource.IsCancellationRequested)
        {
            _ = ObserveAbandonedOperationAsync(operation);
            throw new ApiTimeoutException(timeout.TotalMilliseconds);
        }

        try
        {
            return await operation.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw ClassifyAttemptException(tag, exception, timeout, timeoutSource, callerCancellation);
        }
    }

    private async Task<BufferedResponse> SendAndBufferAsync(
        AttemptState attemptState,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        HttpRequestMessage request = attemptState.Request;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            attemptState.SetResponse(response);
            byte[] body = await ReadBodyAsync(response.Content, cancellationToken).ConfigureAwait(false);
            return new BufferedResponse(
                response.StatusCode,
                response.ReasonPhrase,
                response.Version,
                request.Method,
                request.RequestUri!,
                CopyHeaders(response.Headers),
                CopyHeaders(response.Content.Headers),
                body);
        }
        finally
        {
            response?.Dispose();
            request.Dispose();
        }
    }

    private async Task<byte[]> ReadBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content is null)
        {
            return Array.Empty<byte>();
        }

        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > _maxResponseBodyBytes)
            {
                throw new ApiConnectionException("Response body exceeded the configured buffer limit.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return buffer.ToArray();
    }

    private async Task BackOffAsync(
        string tag,
        int attempt,
        int retriesLeft,
        string reason,
        IReadOnlyDictionary<string, string>? headers,
        RetryPolicy retry,
        CancellationToken cancellationToken)
    {
        double delay = RetryHelpers.CalculateDelayMs(attempt, headers, retry);
        int retryNumber = attempt + 1;
        int total = attempt + retriesLeft;
        Logger.Info(
            $"{tag} retrying in {FormatNumber(delay)}ms (retry {retryNumber}/{total}) after {reason}");
        try
        {
            await RetryHelpers.SleepAsync(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            Logger.Info($"{tag} aborted by caller while waiting to retry");
            throw new ApiUserAbortException(innerException: exception);
        }
    }

    private static HttpRequestMessage CreateRequest(
        Uri url,
        HttpMethod method,
        IReadOnlyDictionary<string, string> headers,
        string? body,
        bool hasBody)
    {
        var request = new HttpRequestMessage(method, url);
        if (hasBody)
        {
            request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body ?? string.Empty));
            request.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        }

        foreach ((string name, string value) in headers)
        {
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            request.Headers.TryAddWithoutValidation(name, value);
        }

        return request;
    }

    private Exception ClassifyAttemptException(
        string tag,
        Exception exception,
        TimeSpan timeout,
        CancellationTokenSource timeoutSource,
        CancellationToken callerCancellation)
    {
        if (callerCancellation.IsCancellationRequested)
        {
            Logger.Info($"{tag} aborted by caller");
            return new ApiUserAbortException(innerException: exception);
        }

        if (timeoutSource.IsCancellationRequested)
        {
            Logger.Info($"{tag} timed out after {timeout.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)}ms");
            return new ApiTimeoutException(timeout.TotalMilliseconds, exception);
        }

        if (exception is ApiConnectionException connection)
        {
            Logger.Info($"{tag} connection error after {exception.Message}");
            return connection;
        }

        Logger.Info($"{tag} connection error", SanitizeForLog(exception.Message));
        return new ApiConnectionException(
            string.IsNullOrEmpty(exception.Message) ? "Connection error." : $"Connection error: {exception.Message}",
            exception);
    }

    private static async Task ObserveAbandonedOperationAsync(Task<BufferedResponse> operation)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static bool IsRetryableConnection(ApiConnectionException exception, RetryPolicy policy) =>
        exception is ApiTimeoutException ? policy.ApiTimeoutError : policy.ApiConnectionError;

    private sealed class AttemptState
    {
        private readonly HttpRequestMessage _request;
        private HttpResponseMessage? _response;
        private int _aborted;

        internal AttemptState(HttpRequestMessage request)
        {
            _request = request;
        }

        internal HttpRequestMessage Request => _request;

        internal void SetResponse(HttpResponseMessage response)
        {
            if (Volatile.Read(ref _aborted) != 0)
            {
                response.Dispose();
                return;
            }

            _response = response;
            if (Volatile.Read(ref _aborted) != 0)
            {
                response.Dispose();
            }
        }

        internal void Abort()
        {
            Interlocked.Exchange(ref _aborted, 1);
            _response?.Dispose();
            _request.Dispose();
        }
    }

    private object? SanitizeForLog(object? value)
    {
        if (value is null)
        {
            return null;
        }

        string text = value is string stringValue
            ? stringValue
            : JsonSerializer.Serialize(value, Json.TypeSafeJson.Options);
        return string.IsNullOrEmpty(_apiKey) || !text.Contains(_apiKey, StringComparison.Ordinal)
            ? value
            : text.Replace(_apiKey, "***", StringComparison.Ordinal);
    }

    private static Dictionary<string, string> CopyHeaders(HttpHeaders headers)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, IEnumerable<string> values) in headers)
        {
            result[name] = string.Join(", ", values);
        }

        return result;
    }

    private static HttpClientHandler CreateDefaultHandler() => new HttpClientHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.All,
    };

    private static HttpClient ConfigureHttpClient(HttpClient httpClient)
    {
        httpClient.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        return httpClient;
    }

    private static TimeSpan ValidateTimeout(TimeSpan timeout, string name)
    {
        if (timeout <= TimeSpan.Zero || timeout == System.Threading.Timeout.InfiniteTimeSpan)
        {
            throw new TypeSafeException(
                $"`{name}` must be a positive number of milliseconds, got {timeout.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)}.");
        }

        return timeout;
    }

    private static string FormatNumber(double value) =>
        value.ToString("0.################", CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && _ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static Task<SystemOneResult> ParseSystemOneAsync(BufferedResponse response)
    {
        JsonNode? root = response.ParseJson();
        if (root is null)
        {
            throw new TypeSafeException("System-one response body was empty or invalid JSON.");
        }

        try
        {
            return Task.FromResult(
                JsonSerializer.Deserialize<SystemOneResult>(root.ToJsonString(), Json.TypeSafeJson.Options)
                ?? throw new TypeSafeException("System-one response body was empty or invalid JSON."));
        }
        catch (JsonException exception)
        {
            throw new TypeSafeException("System-one response body was not a valid response.", exception);
        }
    }
}
