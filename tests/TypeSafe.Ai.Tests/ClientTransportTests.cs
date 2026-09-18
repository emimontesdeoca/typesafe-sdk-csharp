using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TypeSafe.Ai.Tests;

#pragma warning disable CA1707
public sealed class ClientTransportTests
{
    [Fact]
    public async Task CL10_models_list_sends_identity_headers_and_unwraps_models()
    {
        var handler = new RecordingHttpMessageHandler((_, _, _) => Task.FromResult(
            HttpTestResponses.Json(
                "{\"models\":[{\"name\":\"m\",\"description\":\"d\",\"release_date\":\"arbitrary\"}]}",
                headers: ("x-typesafe-request-id", "req_1"))));
        using var httpClient = new HttpClient(handler);
        using var client = new TypeSafeClient(httpClient, Options());

        IReadOnlyList<ModelCard> models = await client.Models.ListAsync();

        Assert.Single(models);
        Assert.Equal("arbitrary", models[0].ReleaseDate);
        CapturedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://x.test/v1/models", request.Uri.AbsoluteUri);
        Assert.Equal("Bearer secret-key", request.Headers["Authorization"]);
        Assert.Equal("application/json", request.Headers["Accept"]);
        Assert.Equal($"typesafe-sdk/{TypeSafeVersion.SdkVersion}", request.Headers["User-Agent"]);
        Assert.Equal($"typesafe-sdk/{TypeSafeVersion.SdkVersion}", request.Headers["X-TypeSafe-SDK"]);
        Assert.False(request.Headers.ContainsKey("Content-Type"));
        Assert.False(request.Headers.ContainsKey("X-TypeSafe-Retry-Count"));
    }

    [Fact]
    public async Task CL15_and_CL26_system_one_preserves_wire_names_nulls_extensions_and_default_model()
    {
        var handler = new RecordingHttpMessageHandler((_, _, _) => Task.FromResult(
            HttpTestResponses.Json(
                "{\"model\":\"jev-latest\",\"answers\":{\"q\":{\"type\":\"noul\",\"noul\":0.5}},\"usage\":{\"input_tokens\":1,\"output_tokens\":2}}")));
        using var httpClient = new HttpClient(handler);
        using var client = new TypeSafeClient(httpClient, Options());

        SystemOneResult result = await client.SystemOneAsync(new SystemOneRequest
        {
            State = null,
            Questions = new Dictionary<string, Question>
            {
                ["q"] = Questions.Noul(),
            },
            ExtensionData = new Dictionary<string, JsonNode?>
            {
                ["future_option"] = null,
            },
        });

        Assert.Equal("jev-latest", result.Model);
        Assert.IsType<NoulAnswer>(result.Answers["q"]);
        CapturedRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("application/json", request.Headers["Content-Type"]);
        using JsonDocument body = JsonDocument.Parse(request.Body!);
        Assert.Equal("jev-latest", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("state").ValueKind);
        Assert.Equal(JsonValueKind.Null,
            body.RootElement.GetProperty("questions").GetProperty("q").GetProperty("instructions").ValueKind);
        Assert.False(body.RootElement.GetProperty("questions").GetProperty("q")
            .TryGetProperty("criteria", out _));
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("future_option").ValueKind);
    }

    [Fact]
    public async Task CL11_and_RR01_protected_headers_win_on_each_retry_and_custom_headers_merge()
    {
        var handler = new RecordingHttpMessageHandler((index, _, _) => Task.FromResult(
            index == 0
                ? HttpTestResponses.Json("{\"error\":\"retry\"}", HttpStatusCode.ServiceUnavailable)
                : HttpTestResponses.Json("{\"models\":[]}")));
        using var httpClient = new HttpClient(handler);
        using var client = new TypeSafeClient(httpClient, new TypeSafeClientOptions
        {
            ApiKey = "secret-key",
            BaseUri = new Uri("https://x.test///"),
            Retry = new RetryPolicy { MaxRetries = 1, BackoffInitialMs = 0, BackoffMaxMs = 0, BackoffJitter = 0 },
            DefaultHeaders = new Dictionary<string, string>
            {
                ["authorization"] = "wrong",
                ["ACCEPT"] = "wrong",
                ["User-Agent"] = "wrong",
                ["x-typesafe-sdk"] = "wrong",
                ["x-typesafe-runtime"] = "wrong",
                ["content-type"] = "wrong",
                ["x-typesafe-retry-count"] = "wrong",
                ["X-Custom"] = "default",
            },
        });

        await client.Models.ListAsync(new RequestOptions
        {
            Headers = new Dictionary<string, string>
            {
                ["x-custom"] = "per-call",
            },
        });

        Assert.Equal(2, handler.Count);
        CapturedRequest first = handler.Requests[0];
        CapturedRequest second = handler.Requests[1];
        foreach (CapturedRequest request in new[] { first, second })
        {
            Assert.Equal("Bearer secret-key", request.Headers["Authorization"]);
            Assert.Equal("application/json", request.Headers["Accept"]);
            Assert.Equal($"typesafe-sdk/{TypeSafeVersion.SdkVersion}", request.Headers["User-Agent"]);
            Assert.Equal($"typesafe-sdk/{TypeSafeVersion.SdkVersion}", request.Headers["X-TypeSafe-SDK"]);
            Assert.NotEqual("wrong", request.Headers["X-TypeSafe-Runtime"]);
            Assert.Equal("per-call", request.Headers["X-Custom"]);
            Assert.False(request.Headers.ContainsKey("Content-Type"));
        }

        Assert.False(first.Headers.ContainsKey("X-TypeSafe-Retry-Count"));
        Assert.Equal("1", second.Headers["X-TypeSafe-Retry-Count"]);
        Assert.Equal("https://x.test/v1/models", first.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task CL16_and_CL17_request_model_override_wins_over_client_default()
    {
        var handler = new RecordingHttpMessageHandler((_, _, _) => Task.FromResult(
            HttpTestResponses.Json(
                "{\"model\":\"m\",\"answers\":{},\"usage\":{\"input_tokens\":0,\"output_tokens\":0}}")));
        using var httpClient = new HttpClient(handler);
        using var client = new TypeSafeClient(httpClient, new TypeSafeClientOptions
        {
            ApiKey = "secret-key",
            BaseUri = new Uri("https://x.test"),
            DefaultModel = "client-default",
            Retry = new RetryPolicy { MaxRetries = 0 },
        });

        await client.SystemOneAsync(new SystemOneRequest
        {
            State = "state",
            Questions = new Dictionary<string, Question> { ["q"] = Questions.Choice("q", new Dictionary<string, EntryValue?> { ["a"] = null }) },
        });
        await client.SystemOneAsync(new SystemOneRequest
        {
            State = "state",
            Questions = new Dictionary<string, Question> { ["q"] = Questions.Choice("q", new Dictionary<string, EntryValue?> { ["a"] = null }) },
            Model = "per-call",
        });

        Assert.Equal("client-default", JsonDocument.Parse(handler.Requests[0].Body!).RootElement.GetProperty("model").GetString());
        Assert.Equal("per-call", JsonDocument.Parse(handler.Requests[1].Body!).RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task CL30_empty_questions_are_rejected_before_the_handler_runs()
    {
        var handler = new RecordingHttpMessageHandler((_, _, _) =>
            Task.FromResult(HttpTestResponses.Json("{}")));
        using var httpClient = new HttpClient(handler);
        using var client = new TypeSafeClient(httpClient, Options());

        await Assert.ThrowsAsync<TypeSafeException>(async () => await client.SystemOneAsync(new SystemOneRequest
        {
            State = "state",
            Questions = new Dictionary<string, Question>(),
        }));
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task CL19_connection_failures_are_wrapped_and_RL05_connection_retries_work()
    {
        var cause = new HttpRequestException("fetch failed");
        var handler = new RecordingHttpMessageHandler((index, _, _) =>
            index == 0
                ? Task.FromException<HttpResponseMessage>(cause)
                : Task.FromResult(HttpTestResponses.Json("{\"models\":[]}")));
        using var httpClient = new HttpClient(handler);
        using var client = new TypeSafeClient(httpClient, new TypeSafeClientOptions
        {
            ApiKey = "secret-key",
            BaseUri = new Uri("https://x.test"),
            Retry = new RetryPolicy { MaxRetries = 1, BackoffInitialMs = 0, BackoffMaxMs = 0, BackoffJitter = 0 },
        });

        await client.Models.ListAsync();
        Assert.Equal(2, handler.Count);

        var noRetryHandler = new RecordingHttpMessageHandler((_, _, _) =>
            Task.FromException<HttpResponseMessage>(cause));
        using var noRetryHttpClient = new HttpClient(noRetryHandler);
        using var noRetryClient = new TypeSafeClient(noRetryHttpClient, new TypeSafeClientOptions
        {
            ApiKey = "secret-key",
            Retry = new RetryPolicy { MaxRetries = 0 },
        });
        ApiConnectionException exception = await Assert.ThrowsAsync<ApiConnectionException>(
            async () => await noRetryClient.Models.ListAsync());
        Assert.Same(cause, exception.InnerException);
    }

    [Fact]
    public async Task CL04_and_LG08_api_keys_are_not_serialized_or_logged()
    {
        var logger = new RecordingLogger();
        const string secret = "super-secret-key";
        var handler = new RecordingHttpMessageHandler((_, _, _) =>
            Task.FromResult(HttpTestResponses.Json("{\"models\":[]}")));
        using var httpClient = new HttpClient(handler);
        var options = new TypeSafeClientOptions
        {
            ApiKey = secret,
            Logger = logger,
            LogLevel = TypeSafeLogLevel.Debug,
        };
        using var client = new TypeSafeClient(httpClient, options);

        await client.Models.ListAsync();
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(options), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(client), StringComparison.Ordinal);
        Assert.DoesNotContain(
            secret,
            string.Join("\n", logger.Entries.SelectMany(entry => new[] { entry.Message }.Concat(entry.Args.Select(arg => arg?.ToString() ?? "")))),
            StringComparison.Ordinal);
    }

    private static TypeSafeClientOptions Options() => new()
    {
        ApiKey = "secret-key",
        BaseUri = new Uri("https://x.test"),
        Retry = new RetryPolicy { MaxRetries = 0 },
    };
}
#pragma warning restore CA1707
