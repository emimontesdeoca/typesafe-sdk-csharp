using System.Runtime.CompilerServices;

namespace TypeSafe.Ai;

/// <summary>Parsed TypeSafe data with shared raw-response access.</summary>
public sealed class ApiPromise<T>
{
    private readonly Task<BufferedResponse> _responseTask;
    private readonly Func<BufferedResponse, Task<T>> _parser;
    private readonly object _parseGate = new();
    private Task<T>? _parsedTask;

    internal ApiPromise(Task<BufferedResponse> responseTask, Func<BufferedResponse, Task<T>> parser)
    {
        _responseTask = responseTask;
        _parser = parser;
    }

    /// <summary>Gets an awaiter for the parsed result.</summary>
    public TaskAwaiter<T> GetAwaiter() => Parse().GetAwaiter();

    /// <summary>
    /// Returns a fresh readable buffered response. The caller owns and must dispose the response.
    /// </summary>
    public async Task<HttpResponseMessage> AsResponseAsync(CancellationToken cancellationToken = default)
    {
        BufferedResponse response = await _responseTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        return response.CreateResponse();
    }

    /// <summary>Returns parsed data with a caller-owned readable response and request ID.</summary>
    public async Task<WithResponse<T>> WithResponseAsync(CancellationToken cancellationToken = default)
    {
        T data = await Parse().WaitAsync(cancellationToken).ConfigureAwait(false);
        BufferedResponse response = await _responseTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new WithResponse<T>(data, response.CreateResponse(), response.RequestId);
    }

    /// <summary>Maps parsed data while sharing the original request and parsed task.</summary>
    public ApiPromise<TResult> Map<TResult>(Func<T, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new ApiPromise<TResult>(_responseTask, async response =>
        {
            T data = await Parse().ConfigureAwait(false);
            return selector(data);
        });
    }

    private Task<T> Parse()
    {
        Task<T>? parsed = Volatile.Read(ref _parsedTask);
        if (parsed is not null)
        {
            return parsed;
        }

        lock (_parseGate)
        {
            return _parsedTask ??= ParseCoreAsync();
        }
    }

    private async Task<T> ParseCoreAsync()
    {
        BufferedResponse response = await _responseTask.ConfigureAwait(false);
        return await _parser(response).ConfigureAwait(false);
    }
}

/// <summary>Parsed data paired with a caller-owned HTTP response.</summary>
public sealed class WithResponse<T> : IDisposable, IAsyncDisposable
{
    internal WithResponse(T data, HttpResponseMessage response, string? requestId)
    {
        Data = data;
        Response = response;
        RequestId = requestId;
    }

    /// <summary>Gets the parsed data.</summary>
    public T Data { get; }

    /// <summary>Gets the readable HTTP response owned by this wrapper.</summary>
    public HttpResponseMessage Response { get; }

    /// <summary>Gets the server request ID, when supplied.</summary>
    public string? RequestId { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        Response.RequestMessage?.Dispose();
        Response.Dispose();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
