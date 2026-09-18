using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace TypeSafe.Ai.Tests;

internal sealed class CapturedRequest
{
    private CapturedRequest(
        HttpMethod method,
        Uri uri,
        IReadOnlyDictionary<string, string> headers,
        string? body)
    {
        Method = method;
        Uri = uri;
        Headers = headers;
        Body = body;
    }

    internal HttpMethod Method { get; }
    internal Uri Uri { get; }
    internal IReadOnlyDictionary<string, string> Headers { get; }
    internal string? Body { get; }

    internal static async Task<CapturedRequest> CreateAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, IEnumerable<string> values) in request.Headers)
        {
            headers[name] = string.Join(", ", values);
        }

        if (request.Content is not null)
        {
            foreach ((string name, IEnumerable<string> values) in request.Content.Headers)
            {
                headers[name] = string.Join(", ", values);
            }
        }

        string? body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new CapturedRequest(request.Method, request.RequestUri!, headers, body);
    }
}

internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<int, CapturedRequest, CancellationToken, Task<HttpResponseMessage>> _responder;
    private readonly ConcurrentQueue<CapturedRequest> _requests = new();
    private int _count;

    internal RecordingHttpMessageHandler(
        Func<int, CapturedRequest, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    internal int Count => Volatile.Read(ref _count);

    internal IReadOnlyList<CapturedRequest> Requests => _requests.ToArray();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CapturedRequest captured = await CapturedRequest.CreateAsync(request, cancellationToken)
            .ConfigureAwait(false);
        int index = Interlocked.Increment(ref _count) - 1;
        _requests.Enqueue(captured);
        return await _responder(index, captured, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class RecordingLogger : ITypeSafeLogger
{
    internal ConcurrentQueue<(string Level, string Message, object?[] Args)> Entries { get; } = new();

    public void Debug(string message, params object?[] args) => Entries.Enqueue(("debug", message, args));
    public void Info(string message, params object?[] args) => Entries.Enqueue(("info", message, args));
    public void Warn(string message, params object?[] args) => Entries.Enqueue(("warn", message, args));
    public void LogError(string message, params object?[] args) => Entries.Enqueue(("error", message, args));
}

internal static class HttpTestResponses
{
    internal static HttpResponseMessage Json(
        string json,
        HttpStatusCode status = HttpStatusCode.OK,
        params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        foreach ((string name, string value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }

    internal static HttpResponseMessage Empty(HttpStatusCode status) => new(status);

    internal static HttpResponseMessage Stream(
        Stream stream,
        HttpStatusCode status,
        params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StreamContent(stream),
        };
        foreach ((string name, string value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }
}

internal sealed class BlockingStream : Stream
{
    private readonly bool _ignoreCancellation;
    private readonly TaskCompletionSource<bool> _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _readStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposeCount;

    internal BlockingStream(bool ignoreCancellation)
    {
        _ignoreCancellation = ignoreCancellation;
    }

    internal Task ReadStarted => _readStarted.Task;
    internal int DisposeCount => Volatile.Read(ref _disposeCount);

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        _readStarted.TrySetResult(true);
        if (_ignoreCancellation)
        {
            await _release.Task.ConfigureAwait(false);
        }
        else
        {
            await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return 0;
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Interlocked.Increment(ref _disposeCount);
            _release.TrySetResult(true);
        }

        base.Dispose(disposing);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class BrokenBodyStream : Stream
{
    private readonly Exception _cause;
    private int _readCount;

    internal BrokenBodyStream(Exception cause)
    {
        _cause = cause;
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _readCount) == 1)
        {
            buffer.Span[0] = (byte)'[';
            return ValueTask.FromResult(1);
        }

        return ValueTask.FromException<int>(_cause);
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
