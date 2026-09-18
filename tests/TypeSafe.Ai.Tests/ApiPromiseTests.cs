using System.Globalization;
using System.Net;

namespace TypeSafe.Ai.Tests;

#pragma warning disable CA1707
public sealed class ApiPromiseTests
{
    [Fact]
    public async Task AP01_AP02_AP03_AP04_AP05_and_AP08_share_the_request_and_response_snapshot()
    {
        var handler = new RecordingHttpMessageHandler((_, _, _) => Task.FromResult(
            HttpTestResponses.Json("{\"models\":[]}", headers: ("x-typesafe-request-id", "req_1"))));
        using var httpClient = new HttpClient(handler);
        using var client = new TypeSafeClient(httpClient, new TypeSafeClientOptions
        {
            ApiKey = "secret-key",
            BaseUri = new Uri("https://x.test"),
            Retry = new RetryPolicy { MaxRetries = 0 },
        });

        ApiPromise<IReadOnlyList<ModelCard>> promise = client.Models.ListAsync();
        ApiPromise<int> mapped = promise.Map(models => models.Count);
        IReadOnlyList<ModelCard> models = await promise;
        int count = await mapped;
        using HttpResponseMessage raw = await promise.AsResponseAsync();
        using WithResponse<IReadOnlyList<ModelCard>> combined = await promise.WithResponseAsync();

        Assert.Empty(models);
        Assert.Equal(0, count);
        Assert.Equal("{\"models\":[]}", await raw.Content.ReadAsStringAsync());
        Assert.Equal("https://x.test/v1/models", raw.RequestMessage!.RequestUri!.AbsoluteUri);
        Assert.Empty(combined.Data);
        Assert.Equal("req_1", combined.RequestId);
        Assert.Equal("req_1", combined.Response.Headers.GetValues("x-typesafe-request-id").Single());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task AP06_custom_promise_parser_runs_once_and_map_reuses_it()
    {
        var response = new BufferedResponse(
            HttpStatusCode.OK,
            null,
            HttpVersion.Version11,
            HttpMethod.Get,
            new Uri("https://x.test/v1/models"),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            Array.Empty<byte>());
        int parses = 0;
        var promise = new ApiPromise<int>(
            Task.FromResult(response),
            _ =>
            {
                parses++;
                return Task.FromResult(42);
            });

        ApiPromise<string> mapped = promise.Map(value => value.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(42, await promise);
        Assert.Equal("42", await mapped);
        Assert.Equal(42, (await promise.WithResponseAsync()).Data);
        Assert.Equal(1, parses);
    }

    [Fact]
    public async Task AP07_http_errors_surface_through_parsed_raw_and_combined_access()
    {
        var handler = new RecordingHttpMessageHandler((_, _, _) => Task.FromResult(
            HttpTestResponses.Json("{\"message\":\"missing\"}", HttpStatusCode.NotFound)));
        using var httpClient = new HttpClient(handler);
        using var client = new TypeSafeClient(httpClient, new TypeSafeClientOptions
        {
            ApiKey = "secret-key",
            BaseUri = new Uri("https://x.test"),
            Retry = new RetryPolicy { MaxRetries = 0 },
        });
        ApiPromise<IReadOnlyList<ModelCard>> promise = client.Models.ListAsync();

        await Assert.ThrowsAsync<NotFoundException>(async () => await promise);
        await Assert.ThrowsAsync<NotFoundException>(() => promise.AsResponseAsync());
        await Assert.ThrowsAsync<NotFoundException>(() => promise.WithResponseAsync());
        Assert.Equal(1, handler.Count);
    }
}
#pragma warning restore CA1707
