using System.Net;

namespace TypeSafe.Ai.Tests;

#pragma warning disable CA1707
public sealed class TransportEdgeTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task NT02_and_RR04_stalled_bodies_timeout_and_are_disposed(HttpStatusCode status)
    {
        var stream = new BlockingStream(ignoreCancellation: true);
        var handler = new RecordingHttpMessageHandler((_, _, _) =>
            Task.FromResult(HttpTestResponses.Stream(stream, status)));
        using var httpClient = new HttpClient(handler);
        using var client = new TypeSafeClient(httpClient, new TypeSafeClientOptions
        {
            ApiKey = "secret-key",
            BaseUri = new Uri("https://x.test"),
            Timeout = TimeSpan.FromMilliseconds(50),
            Retry = new RetryPolicy { MaxRetries = 0 },
        });

        await Assert.ThrowsAsync<ApiTimeoutException>(async () => await client.Models.ListAsync());
        Assert.True(stream.DisposeCount > 0);
    }

    [Fact]
    public async Task RL12_each_retry_gets_a_fresh_timeout_budget()
    {
        var firstStream = new BlockingStream(ignoreCancellation: true);
        var handler = new RecordingHttpMessageHandler((index, _, _) =>
            Task.FromResult(index == 0
                ? HttpTestResponses.Stream(firstStream, HttpStatusCode.OK)
                : HttpTestResponses.Json("{\"models\":[]}")));
        using var httpClient = new HttpClient(handler);
        using var client = new TypeSafeClient(httpClient, new TypeSafeClientOptions
        {
            ApiKey = "secret-key",
            BaseUri = new Uri("https://x.test"),
            Timeout = TimeSpan.FromMilliseconds(40),
            Retry = new RetryPolicy { MaxRetries = 1, BackoffInitialMs = 0, BackoffMaxMs = 0, BackoffJitter = 0 },
        });

        IReadOnlyList<ModelCard> models = await client.Models.ListAsync();

        Assert.Empty(models);
        Assert.Equal(2, handler.Count);
        Assert.True(firstStream.DisposeCount > 0);
    }

    [Fact]
    public async Task NT03_caller_cancellation_after_headers_is_user_abort_and_never_retries()
    {
        var stream = new BlockingStream(ignoreCancellation: true);
        var handler = new RecordingHttpMessageHandler((_, _, _) =>
            Task.FromResult(HttpTestResponses.Stream(stream, HttpStatusCode.OK)));
        using var httpClient = new HttpClient(handler);
        using var client = new TypeSafeClient(httpClient, new TypeSafeClientOptions
        {
            ApiKey = "secret-key",
            BaseUri = new Uri("https://x.test"),
            Retry = new RetryPolicy { MaxRetries = 2, BackoffInitialMs = 0, BackoffMaxMs = 0 },
        });
        using var cancellation = new CancellationTokenSource();
        ApiPromise<IReadOnlyList<ModelCard>> promise = client.Models.ListAsync(
            cancellationToken: cancellation.Token);
        await stream.ReadStarted;
        cancellation.Cancel();

        await Assert.ThrowsAsync<ApiUserAbortException>(async () => await promise);
        Assert.Equal(1, handler.Count);
        Assert.True(stream.DisposeCount > 0);
    }

    [Fact]
    public async Task NT04_and_RR05_broken_body_retries_and_exposes_second_response_metadata()
    {
        var cause = new IOException("socket dropped");
        var handler = new RecordingHttpMessageHandler((index, _, _) =>
            Task.FromResult(index == 0
                ? HttpTestResponses.Stream(new BrokenBodyStream(cause), HttpStatusCode.OK,
                    ("x-typesafe-request-id", "req_1"))
                : HttpTestResponses.Json("{\"models\":[]}", headers: ("x-typesafe-request-id", "req_2"))));
        using var httpClient = new HttpClient(handler);
        using var client = new TypeSafeClient(httpClient, new TypeSafeClientOptions
        {
            ApiKey = "secret-key",
            BaseUri = new Uri("https://x.test"),
            Retry = new RetryPolicy { MaxRetries = 1, BackoffInitialMs = 0, BackoffMaxMs = 0, BackoffJitter = 0 },
        });

        using WithResponse<IReadOnlyList<ModelCard>> response = await client.Models.ListAsync().WithResponseAsync();

        Assert.Empty(response.Data);
        Assert.Equal("req_2", response.RequestId);
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task RR05_disabled_connection_retries_surface_the_body_failure_cause()
    {
        var cause = new IOException("socket dropped");
        var handler = new RecordingHttpMessageHandler((_, _, _) =>
            Task.FromResult(HttpTestResponses.Stream(new BrokenBodyStream(cause), HttpStatusCode.OK)));
        using var httpClient = new HttpClient(handler);
        using var client = new TypeSafeClient(httpClient, new TypeSafeClientOptions
        {
            ApiKey = "secret-key",
            Retry = new RetryPolicy { MaxRetries = 1, ApiConnectionError = false },
        });

        ApiConnectionException exception = await Assert.ThrowsAsync<ApiConnectionException>(
            async () => await client.Models.ListAsync());
        Assert.Equal(1, handler.Count);
        Assert.Same(cause, exception.InnerException);
    }

    [Fact]
    public async Task RR07_null_body_raw_response_remains_readable()
    {
        var handler = new RecordingHttpMessageHandler((_, _, _) =>
            Task.FromResult(HttpTestResponses.Empty(HttpStatusCode.NoContent)));
        using var httpClient = new HttpClient(handler);
        using var client = new TypeSafeClient(httpClient, new TypeSafeClientOptions
        {
            ApiKey = "secret-key",
            Retry = new RetryPolicy { MaxRetries = 0 },
        });

        using HttpResponseMessage response = await client.Models.ListAsync().AsResponseAsync();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"models\":{\"models\":[]}}")]
    [InlineData("{\"models\":null}")]
    [InlineData("{\"models\":\"bad\"}")]
    [InlineData("{\"ok\":true}")]
    public async Task CL13_malformed_model_shapes_raise_the_documented_validation_error(string json)
    {
        var handler = new RecordingHttpMessageHandler((_, _, _) =>
            Task.FromResult(HttpTestResponses.Json(json)));
        using var httpClient = new HttpClient(handler);
        using var client = new TypeSafeClient(httpClient, new TypeSafeClientOptions
        {
            ApiKey = "secret-key",
            Retry = new RetryPolicy { MaxRetries = 0 },
        });

        TypeSafeException exception = await Assert.ThrowsAsync<TypeSafeException>(
            async () => await client.Models.ListAsync());
        Assert.StartsWith("Unexpected response shape from GET /v1/models", exception.Message, StringComparison.Ordinal);
    }
}
#pragma warning restore CA1707
