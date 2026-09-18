using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TypeSafe.Ai.Json;

namespace TypeSafe.Ai.Tests;

#pragma warning disable CA1707
public sealed class AdditionalCoverageTests
{
    private static readonly TypeSafeLogLevel[] LogLevels =
    {
        TypeSafeLogLevel.Debug,
        TypeSafeLogLevel.Info,
        TypeSafeLogLevel.Warn,
        TypeSafeLogLevel.Error,
        TypeSafeLogLevel.Off,
    };

    [Fact]
    public void TY02_entry_values_cover_top_level_shapes_and_converter_nulls()
    {
        EntryValue text = EntryValue.FromString("text");
        EntryValue array = EntryValue.FromArray(new JsonArray(1, false));
        EntryValue @object = EntryValue.FromObject(new JsonObject { ["x"] = 1 });

        Assert.Equal(EntryValueKind.StringValue, text.Kind);
        Assert.Equal(EntryValueKind.ArrayValue, array.Kind);
        Assert.Equal(EntryValueKind.ObjectValue, @object.Kind);
        Assert.Equal(EntryValue.Null, EntryValue.Parse("null"));
        Assert.Equal(EntryValueKind.StringValue, EntryValue.Parse("\"parsed\"").Kind);
        Assert.Equal(EntryValueKind.ArrayValue, EntryValue.Parse("[1,false]").Kind);
        Assert.Equal(EntryValueKind.ObjectValue, EntryValue.Parse("{\"x\":1}").Kind);
        Assert.Equal("null", EntryValue.Null.ToString());
        Assert.True(text.Equals(EntryValue.FromString("text")));
        Assert.False(text.Equals(null));
        Assert.NotEqual(0, text.GetHashCode());

        Assert.Equal("null", JsonSerializer.Serialize<EntryValue?>(null, TypeSafeJson.Options));
        Assert.Equal("null", JsonSerializer.Serialize<EntryValue>(EntryValue.Null, TypeSafeJson.Options));
        Assert.Equal(EntryValueKind.StringValue,
            JsonSerializer.Deserialize<EntryValue>("\"wire\"", TypeSafeJson.Options)!.Kind);
        Assert.Equal(EntryValueKind.Null,
            JsonSerializer.Deserialize<EntryValue>("null", TypeSafeJson.Options)!.Kind);

        Assert.Throws<ArgumentNullException>(() => EntryValue.FromString(null!));
        Assert.Throws<ArgumentNullException>(() => EntryValue.FromObject(null!));
        Assert.Throws<ArgumentNullException>(() => EntryValue.FromArray(null!));
        Assert.Throws<ArgumentNullException>(() => EntryValue.Parse(null!));
    }

    [Fact]
    public void TY03_question_converter_covers_write_read_and_malformed_discriminators()
    {
        ChoiceQuestion choice = Questions.Choice("pick", new Dictionary<string, EntryValue?>
        {
            ["a"] = null,
            ["b"] = "description",
        });
        ScoreQuestion score = Questions.Score("score", new EntryValue?[] { "low", null, "high" });
        NoulQuestion noul = Questions.Noul("noul", new NoulCriteria { True = "yes", False = null });

        string choiceJson = JsonSerializer.Serialize<Question>(choice, TypeSafeJson.Options);
        string scoreJson = JsonSerializer.Serialize<Question>(score, TypeSafeJson.Options);
        string noulJson = JsonSerializer.Serialize<Question>(noul, TypeSafeJson.Options);
        Assert.Contains("\"criteria\":{", choiceJson, StringComparison.Ordinal);
        Assert.Contains("\"criteria\":[", scoreJson, StringComparison.Ordinal);
        Assert.Contains("\"false\":null", noulJson, StringComparison.Ordinal);
        Assert.IsType<ChoiceQuestion>(JsonSerializer.Deserialize<Question>(choiceJson, TypeSafeJson.Options));
        Assert.IsType<ScoreQuestion>(JsonSerializer.Deserialize<Question>(scoreJson, TypeSafeJson.Options));
        Assert.IsType<NoulQuestion>(JsonSerializer.Deserialize<Question>(noulJson, TypeSafeJson.Options));

        string[] malformed =
        {
            "null",
            "[]",
            "{}",
            "{\"type\":\"unknown\"}",
            "{\"type\":\"noul\",\"criteria\":[]}",
            "{\"type\":\"choice\"}",
            "{\"type\":\"score\"}",
        };
        foreach (string json in malformed)
        {
            Assert.Throws<TypeSafeException>(() => JsonSerializer.Deserialize<Question>(json, TypeSafeJson.Options));
        }

        Assert.Throws<ArgumentNullException>(() => Questions.Choice("q", null!));
        Assert.Throws<ArgumentNullException>(() => Questions.Score("q", null!));
        Assert.Throws<TypeSafeException>(() => Questions.Score("q", new EntryValue?[] { "only" }));
        Assert.Throws<TypeSafeException>(() => Questions.Validate(new Dictionary<string, Question>
        {
            ["null"] = null!,
            ["score"] = score,
        }));
        Assert.Throws<TypeSafeException>(() => Questions.Validate(new Dictionary<string, Question>
        {
            ["score"] = new ScoreQuestion { Criteria = new EntryValue?[] { "only" } },
        }));
    }

    [Fact]
    public void TY03_answers_cover_all_discriminators_and_wire_round_trips()
    {
        const string scoreJson = "{\"type\":\"score\",\"score\":1.5,\"confidence\":0.8,\"legend\":{\"0\":\"low\",\"1\":null},\"probabilities\":{\"0\":0.2,\"1\":0.8}}";
        ScoreAnswer score = Assert.IsType<ScoreAnswer>(
            JsonSerializer.Deserialize<Answer>(scoreJson, TypeSafeJson.Options));
        ChoiceAnswer choice = new()
        {
            Choice = "b",
            Confidence = 0.9,
            Probabilities = new Dictionary<string, double> { ["a"] = 0.1, ["b"] = 0.9 },
        };
        NoulAnswer noul = new() { Noul = 0.5 };

        Assert.Equal(1.5, score.Score);
        Assert.Null(score.Legend[1]);
        Assert.IsType<ChoiceAnswer>(JsonSerializer.Deserialize<Answer>(
            JsonSerializer.Serialize<Answer>(choice, TypeSafeJson.Options), TypeSafeJson.Options));
        Assert.IsType<NoulAnswer>(JsonSerializer.Deserialize<Answer>(
            JsonSerializer.Serialize<Answer>(noul, TypeSafeJson.Options), TypeSafeJson.Options));
        string encodedScore = JsonSerializer.Serialize<Answer>(score, TypeSafeJson.Options);
        Assert.Contains("\"legend\":", encodedScore, StringComparison.Ordinal);
        Assert.Contains("\"probabilities\":", encodedScore, StringComparison.Ordinal);

        string[] malformed =
        {
            "null",
            "[]",
            "{}",
            "{\"type\":\"unknown\"}",
            "{\"type\":\"noul\"}",
            "{\"type\":\"choice\",\"choice\":\"a\"}",
            "{\"type\":\"score\",\"score\":1,\"confidence\":0,\"legend\":{\"bad\":\"x\"},\"probabilities\":{}}",
        };
        foreach (string json in malformed)
        {
            Assert.Throws<TypeSafeException>(() => JsonSerializer.Deserialize<Answer>(json, TypeSafeJson.Options));
        }
    }

    [Fact]
    public void CL32_request_and_response_converters_preserve_extensions_and_optional_fields()
    {
        const string requestJson = "{\"state\":{\"x\":1},\"questions\":{\"q\":{\"type\":\"noul\",\"instructions\":null}},\"model\":\"m\",\"future\":{\"enabled\":true}}";
        SystemOneRequest request = JsonSerializer.Deserialize<SystemOneRequest>(requestJson, TypeSafeJson.Options)!;
        Assert.Equal("m", request.Model);
        Assert.True(request.ExtensionData.ContainsKey("future"));
        string encodedRequest = JsonSerializer.Serialize(request, TypeSafeJson.Options);
        Assert.Contains("\"future\":{\"enabled\":true}", encodedRequest, StringComparison.Ordinal);

        var result = new SystemOneResult
        {
            Model = "m",
            Answers = new Dictionary<string, Answer> { ["q"] = new NoulAnswer { Noul = 0.7 } },
            Usage = new Usage { InputTokens = 3, OutputTokens = 4 },
        };
        string encodedResult = JsonSerializer.Serialize(result, TypeSafeJson.Options);
        Assert.Contains("input_tokens", encodedResult, StringComparison.Ordinal);
        Assert.Equal("m", JsonSerializer.Deserialize<SystemOneResult>(encodedResult, TypeSafeJson.Options)!.Model);
        Assert.Empty(JsonSerializer.Deserialize<SystemOneResult>("{\"model\":\"m\"}", TypeSafeJson.Options)!.Answers);
    }

    [Fact]
    public void CL08_and_CL09_log_levels_are_parsed_and_console_sink_methods_are_callable()
    {
        foreach (TypeSafeLogLevel level in LogLevels)
        {
            string text = TypeSafeLogLevelParser.Format(level);
            Assert.Equal(level, TypeSafeLogLevelParser.Parse(text, "test"));
        }

        TypeSafeException invalid = Assert.Throws<TypeSafeException>(
            () => TypeSafeLogLevelParser.Parse("verbose", "TYPESAFE_LOG_LEVEL"));
        Assert.Equal(
            "Invalid log level \"verbose\" from TYPESAFE_LOG_LEVEL. Expected one of: debug, info, warn, error, off.",
            invalid.Message);

        var logger = new ConsoleTypeSafeLogger();
        logger.Debug("debug");
        logger.Info("info");
        logger.Warn("warn");
        logger.LogError("error");
        logger.Debug("debug", "detail");
        logger.Info("info", "detail");
        logger.Warn("warn", "detail");
        logger.LogError("error", "detail");
    }

    [Fact]
    public async Task RT03_and_RL10_retry_helpers_cover_invalid_headers_random_and_attempts()
    {
        Assert.Equal(250, RetryHelpers.ParseRetryAfter(new Dictionary<string, string>
        {
            ["retry-after-ms"] = "nope",
            ["retry-after"] = "0.25",
        }));
        Assert.Throws<TypeSafeException>(() => RetryHelpers.CalculateDelayMs(
            0, null, RetryPolicy.Default, () => double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => RetryHelpers.CalculateDelayMs(
            -1, null, RetryPolicy.Default, () => 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await RetryHelpers.SleepAsync(-1));
        Assert.True(RetryHelpers.IsRetryableStatus(503, new RetryPolicy { HttpStatuses = new HashSet<int>() }) == false);
    }

    [Fact]
    public async Task AP03_and_RR07_buffered_response_preserves_headers_content_and_disposal_paths()
    {
        var buffered = new BufferedResponse(
            HttpStatusCode.OK,
            "OK",
            HttpVersion.Version11,
            HttpMethod.Get,
            new Uri("https://x.test/v1/models"),
            new Dictionary<string, string> { ["x-test"] = "yes" },
            new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            Encoding.UTF8.GetBytes("{\"ok\":true}"));
        using HttpResponseMessage raw = buffered.CreateResponse();
        Assert.Equal("yes", raw.Headers.GetValues("x-test").Single());
        Assert.Equal("application/json", raw.Content.Headers.ContentType!.MediaType);
        Assert.NotNull(buffered.ParseJson());
        Assert.True(ErrorBodyParser.Parse(string.Empty).HasBody == false);

        await using WithResponse<int> combined = await new ApiPromise<int>(
            Task.FromResult(buffered), _ => Task.FromResult(1)).WithResponseAsync();
        Assert.Equal(1, combined.Data);
    }

    [Fact]
    public void CL01_and_CL10_invalid_client_configuration_is_rejected_without_transport()
    {
        Assert.Throws<TypeSafeException>(() => new TypeSafeClient(new TypeSafeClientOptions
        {
            ApiKey = "k",
            BaseUri = new Uri("file:///tmp"),
        }));
        Assert.Throws<TypeSafeException>(() => new TypeSafeClient(new TypeSafeClientOptions
        {
            ApiKey = "k",
            Timeout = TimeSpan.Zero,
        }));
        Assert.Throws<TypeSafeException>(() => new TypeSafeClient(new TypeSafeClientOptions
        {
            ApiKey = "k",
            MaxResponseBodyBytes = 0,
        }));

        using var client = new TypeSafeClient(new TypeSafeClientOptions { ApiKey = "k" });
        client.Dispose();
        client.Dispose();
        Assert.Throws<ObjectDisposedException>(() => client.Models.ListAsync());
    }

    [Fact]
    public void CL31_injected_http_client_timeout_is_owned_by_the_sdk()
    {
        using var httpClient = new HttpClient(new RecordingHttpMessageHandler((_, _, _) =>
            Task.FromResult(HttpTestResponses.Json("{\"models\":[]}"))))
        {
            Timeout = TimeSpan.FromSeconds(1),
        };
        using var client = new TypeSafeClient(httpClient, new TypeSafeClientOptions { ApiKey = "k" });

        Assert.Equal(System.Threading.Timeout.InfiniteTimeSpan, httpClient.Timeout);
    }

    [Fact]
    public void CL02_missing_environment_values_are_treated_as_unset()
    {
        const string name = "TYPESAFE_MISSING_TEST_VALUE";
        string? original = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, null);
            Assert.Null(TypeSafeEnvironment.Read(name));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, original);
        }
    }

    [Fact]
    public void LG04_filtered_logger_routes_each_level_and_off_drops_everything()
    {
        var expectedCounts = new Dictionary<TypeSafeLogLevel, int>
        {
            [TypeSafeLogLevel.Debug] = 4,
            [TypeSafeLogLevel.Info] = 3,
            [TypeSafeLogLevel.Warn] = 2,
            [TypeSafeLogLevel.Error] = 1,
            [TypeSafeLogLevel.Off] = 0,
        };

        foreach ((TypeSafeLogLevel level, int expectedCount) in expectedCounts)
        {
            var sink = new RecordingLogger();
            var logger = new FilteredTypeSafeLogger(sink, level);
            logger.Debug("debug");
            logger.Info("info");
            logger.Warn("warn");
            logger.LogError("error");
            Assert.Equal(expectedCount, sink.Entries.Count);
        }
    }

    [Fact]
    public void RT10_retry_policy_resolution_copies_and_overrides_every_field()
    {
        var basePolicy = new RetryPolicy
        {
            MaxRetries = 4,
            BackoffInitialMs = 10,
            BackoffMaxMs = 20,
            BackoffJitter = 0.1,
            HttpStatuses = new HashSet<int> { 409 },
            RespectRetryAfter = false,
            MaxRetryAfterMs = 30,
            ApiConnectionError = false,
            ApiTimeoutError = false,
        };
        var statuses = new HashSet<int> { 503 };
        RetryPolicy resolved = basePolicy.Resolve(new RetryOptions
        {
            MaxRetries = 1,
            BackoffInitialMs = 2,
            BackoffMaxMs = 3,
            BackoffJitter = 0.2,
            HttpStatuses = statuses,
            RespectRetryAfter = true,
            MaxRetryAfterMs = 4,
            ApiConnectionError = true,
            ApiTimeoutError = true,
        });
        statuses.Add(504);

        Assert.Equal(1, resolved.MaxRetries);
        Assert.Equal(2, resolved.BackoffInitialMs);
        Assert.Equal(3, resolved.BackoffMaxMs);
        Assert.Equal(0.2, resolved.BackoffJitter);
        Assert.True(resolved.RespectRetryAfter);
        Assert.Equal(4, resolved.MaxRetryAfterMs);
        Assert.True(resolved.ApiConnectionError);
        Assert.True(resolved.ApiTimeoutError);
        Assert.DoesNotContain(504, resolved.HttpStatuses);
        RetryOptions converted = resolved;
        Assert.Equal(resolved.MaxRetries, converted.MaxRetries);
    }

    [Fact]
    public void CL10_client_constructor_rejects_conflicting_transport_sources()
    {
        using var explicitHttpClient = new HttpClient(new RecordingHttpMessageHandler((_, _, _) =>
            Task.FromResult(HttpTestResponses.Json("{\"models\":[]}"))));
        using var optionHttpClient = new HttpClient(new RecordingHttpMessageHandler((_, _, _) =>
            Task.FromResult(HttpTestResponses.Json("{\"models\":[]}"))));
        using var optionHandler = new RecordingHttpMessageHandler((_, _, _) =>
            Task.FromResult(HttpTestResponses.Json("{\"models\":[]}")));

        Assert.Throws<ArgumentException>(() => new TypeSafeClient(explicitHttpClient, new TypeSafeClientOptions
        {
            ApiKey = "k",
            HttpClient = optionHttpClient,
        }));
        Assert.Throws<ArgumentException>(() => new TypeSafeClient(optionHandler, new TypeSafeClientOptions
        {
            ApiKey = "k",
            HttpMessageHandler = new RecordingHttpMessageHandler((_, _, _) =>
                Task.FromResult(HttpTestResponses.Json("{\"models\":[]}"))),
        }));

        using var client = new TypeSafeClient(new TypeSafeClientOptions
        {
            ApiKey = "k",
            HttpClient = optionHttpClient,
        });
        Assert.Equal(TypeSafeLogLevel.Warn, client.LogLevel);
    }

    [Fact]
    public void TY04_null_root_values_are_rejected_or_written_as_json_null()
    {
        Assert.Equal("null", JsonSerializer.Serialize<Question?>(null, TypeSafeJson.Options));
        Assert.Equal("null", JsonSerializer.Serialize<Answer?>(null, TypeSafeJson.Options));
        Assert.Equal("null", JsonSerializer.Serialize<SystemOneRequest?>(null, TypeSafeJson.Options));
        Assert.Equal("null", JsonSerializer.Serialize<SystemOneResult?>(null, TypeSafeJson.Options));
        Assert.Throws<TypeSafeException>(() => JsonSerializer.Deserialize<Question>("null", TypeSafeJson.Options));
        Assert.Throws<TypeSafeException>(() => JsonSerializer.Deserialize<Answer>("null", TypeSafeJson.Options));
        Assert.Throws<TypeSafeException>(() => JsonSerializer.Deserialize<SystemOneRequest>("null", TypeSafeJson.Options));
        Assert.Throws<TypeSafeException>(() => JsonSerializer.Deserialize<SystemOneResult>("null", TypeSafeJson.Options));

        SystemOneResult result = JsonSerializer.Deserialize<SystemOneResult>(
            "{\"answers\":null,\"usage\":null}", TypeSafeJson.Options)!;
        Assert.Empty(result.Answers);
        Assert.Equal(0, result.Usage.InputTokens);
    }

    [Fact]
    public async Task RL07_caller_abort_during_backoff_is_wrapped_as_user_abort()
    {
        var responseStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHttpMessageHandler((_, _, _) =>
        {
            responseStarted.TrySetResult(true);
            return Task.FromResult(HttpTestResponses.Json("{}", HttpStatusCode.ServiceUnavailable));
        });
        using var httpClient = new HttpClient(handler);
        using var client = new TypeSafeClient(httpClient, new TypeSafeClientOptions
        {
            ApiKey = "k",
            Retry = new RetryPolicy { MaxRetries = 1, BackoffInitialMs = 1_000, BackoffMaxMs = 1_000, BackoffJitter = 0 },
        });
        using var cancellation = new CancellationTokenSource();
        ApiPromise<IReadOnlyList<ModelCard>> pending = client.Models.ListAsync(
            cancellationToken: cancellation.Token);
        await responseStarted.Task;
        cancellation.Cancel();

        await Assert.ThrowsAsync<ApiUserAbortException>(async () => await pending);
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task RL15_rate_limit_exposes_retry_after_and_RR04_bounds_response_buffering()
    {
        var rateHandler = new RecordingHttpMessageHandler((_, _, _) =>
            Task.FromResult(HttpTestResponses.Json("{}", (HttpStatusCode)429, ("retry-after", "7"))));
        using var rateHttpClient = new HttpClient(rateHandler);
        using var rateClient = new TypeSafeClient(rateHttpClient, new TypeSafeClientOptions
        {
            ApiKey = "k",
            Retry = new RetryPolicy { MaxRetries = 0 },
        });
        RateLimitException rate = await Assert.ThrowsAsync<RateLimitException>(
            async () => await rateClient.Models.ListAsync());
        Assert.Equal(7_000, rate.RetryAfterMs);

        var largeHandler = new RecordingHttpMessageHandler((_, _, _) =>
            Task.FromResult(HttpTestResponses.Json("{\"models\":[]}")));
        using var largeHttpClient = new HttpClient(largeHandler);
        using var largeClient = new TypeSafeClient(largeHttpClient, new TypeSafeClientOptions
        {
            ApiKey = "k",
            MaxResponseBodyBytes = 1,
            Retry = new RetryPolicy { MaxRetries = 0 },
        });
        await Assert.ThrowsAsync<ApiConnectionException>(async () => await largeClient.Models.ListAsync());
    }

    [Fact]
    public async Task CL13_invalid_model_card_values_raise_the_shape_error()
    {
        var handler = new RecordingHttpMessageHandler((_, _, _) =>
            Task.FromResult(HttpTestResponses.Json("{\"models\":[{\"name\":1}]}")));
        using var httpClient = new HttpClient(handler);
        using var client = new TypeSafeClient(httpClient, new TypeSafeClientOptions
        {
            ApiKey = "k",
            Retry = new RetryPolicy { MaxRetries = 0 },
        });

        await Assert.ThrowsAsync<TypeSafeException>(async () => await client.Models.ListAsync());
    }

    [Fact]
    public void ER08_buffered_invalid_json_and_empty_message_fallbacks_are_preserved()
    {
        var buffered = new BufferedResponse(
            HttpStatusCode.BadRequest,
            null,
            HttpVersion.Version11,
            HttpMethod.Get,
            new Uri("https://x.test"),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            Encoding.UTF8.GetBytes("not-json"));
        Assert.Null(buffered.ParseJson());
        Assert.True(buffered.ParseBody().HasBody);
        Assert.Equal("400 ", ApiException.FromResponse(400, string.Empty, new Dictionary<string, string>()).Message);
        Assert.Equal("400 []", ApiException.FromResponse(400, new JsonArray(), new Dictionary<string, string>()).Message);
        Assert.Equal("400 {}", ApiException.FromResponse(400, new JsonObject(), new Dictionary<string, string>()).Message);
        Assert.Equal("400 raw", ApiException.FromResponse(400, "raw", new Dictionary<string, string>()).Message);

        var validation = new JsonObject
        {
            ["detail"] = new JsonArray
            {
                new JsonObject { ["msg"] = "plain" },
                new JsonObject { ["msg"] = "empty path", ["loc"] = new JsonArray() },
                JsonValue.Create(1),
            },
        };
        Assert.Equal("422 plain; empty path", ApiException.FromResponse(
            422, validation, new Dictionary<string, string>()).Message);
    }

    [Fact]
    public void CL26_request_nulls_and_missing_optional_fields_round_trip_explicitly()
    {
        const string json = "{\"state\":null,\"questions\":{},\"model\":null,\"future\":null}";
        SystemOneRequest request = JsonSerializer.Deserialize<SystemOneRequest>(json, TypeSafeJson.Options)!;
        string encoded = JsonSerializer.Serialize(request, TypeSafeJson.Options);
        using JsonDocument document = JsonDocument.Parse(encoded);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("state").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("model").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("future").ValueKind);
    }

    [Fact]
    public void AP03_combined_response_disposes_when_no_request_message_exists()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        using var combined = new WithResponse<int>(1, response, null);
        combined.Dispose();
    }
}
#pragma warning restore CA1707
