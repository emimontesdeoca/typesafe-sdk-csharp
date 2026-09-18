using System.Text.Json;

namespace TypeSafe.Ai.Tests;

#pragma warning disable CA1707
public sealed class LoggingRuntimeTests
{
    [Fact]
    public void LG13_and_LG14_redaction_preserves_scheme_suffix_and_input()
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer test_secret_0123456789abcdef",
            ["x-api-key"] = "0123456789abcdef",
            ["Cookie"] = "session=abc",
            ["Accept"] = "application/json",
        };

        IReadOnlyDictionary<string, string> redacted = TypeSafeLogRedaction.RedactHeaders(headers);

        Assert.Equal("Bearer ***cdef", redacted["Authorization"]);
        Assert.Equal("***cdef", redacted["x-api-key"]);
        Assert.Equal("***", redacted["Cookie"]);
        Assert.Equal("application/json", redacted["Accept"]);
        Assert.Equal("Bearer test_secret_0123456789abcdef", headers["Authorization"]);
        Assert.Equal("Bearer ***", TypeSafeLogRedaction.RedactHeaders(
            new Dictionary<string, string> { ["authorization"] = "Bearer abc" })["authorization"]);
    }

    [Fact]
    public async Task LG01_LG04_LG05_LG06_and_LG07_filter_and_shape_logger_events()
    {
        var logger = new RecordingLogger();
        var handler = new RecordingHttpMessageHandler((_, _, _) =>
            Task.FromResult(HttpTestResponses.Json("{\"models\":[]}", headers: ("x-typesafe-request-id", "req_9"))));
        using var httpClient = new HttpClient(handler);
        using var client = new TypeSafeClient(httpClient, new TypeSafeClientOptions
        {
            ApiKey = "test_secret_0123456789abcdef",
            Logger = logger,
            LogLevel = TypeSafeLogLevel.Debug,
        });

        await client.Models.ListAsync();

        Assert.Equal("debug", logger.Entries.First().Level);
        Assert.Contains("Authorization", JsonSerializer.Serialize(logger.Entries.First().Args));
        Assert.Contains("***cdef", JsonSerializer.Serialize(logger.Entries.First().Args));
        Assert.Contains("req_9", string.Join("\n", logger.Entries.Select(entry => entry.Message)));
        Assert.DoesNotContain("test_secret_0123456789abcdef", JsonSerializer.Serialize(logger.Entries));

        var filtered = new RecordingLogger();
        using var filteredHttpClient = new HttpClient(new RecordingHttpMessageHandler((_, _, _) =>
            Task.FromResult(HttpTestResponses.Json("{\"models\":[]}"))));
        using var filteredClient = new TypeSafeClient(filteredHttpClient, new TypeSafeClientOptions
        {
            ApiKey = "k",
            Logger = filtered,
            LogLevel = TypeSafeLogLevel.Warn,
        });
        filteredClient.Logger.Info("dropped");
        filteredClient.Logger.Warn("kept");
        Assert.Single(filtered.Entries);
        Assert.Equal("kept", filtered.Entries.Single().Message);
    }

    [Fact]
    public async Task LG09_and_VM01_request_numbers_and_runtime_header_are_stable()
    {
        var logger = new RecordingLogger();
        var handler = new RecordingHttpMessageHandler(async (_, _, _) =>
        {
            await Task.Yield();
            return HttpTestResponses.Json("{\"models\":[]}");
        });
        using var httpClient = new HttpClient(handler);
        using var client = new TypeSafeClient(httpClient, new TypeSafeClientOptions
        {
            ApiKey = "k",
            Logger = logger,
            LogLevel = TypeSafeLogLevel.Info,
        });

        HttpResponseMessage[] responses = await Task.WhenAll(
            client.Models.ListAsync().AsResponseAsync(),
            client.Models.ListAsync().AsResponseAsync());
        foreach (HttpResponseMessage response in responses)
        {
            response.Dispose();
        }

        Assert.Contains(logger.Entries, entry => entry.Message.StartsWith("#1 ", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Message.StartsWith("#2 ", StringComparison.Ordinal));
        Assert.Matches("^dotnet/.+ \\(\\w+; \\w+\\)$", handler.Requests[0].Headers["X-TypeSafe-Runtime"]);
    }
}
#pragma warning restore CA1707
