using System.Globalization;
using System.Text.Json.Nodes;

namespace TypeSafe.Ai.Tests;

#pragma warning disable CA1707
public sealed class GateCTests
{
    private static readonly int[] ExpectedDefaultStatuses = new[] { 408, 429 }
        .Concat(Enumerable.Range(500, 100))
        .ToArray();
    private static readonly double[] ExpectedBackoffDelays = { 500, 1_000, 2_000, 4_000, 5_000, 5_000 };

    [Fact]
    public void RT01_default_retry_policy_matches_the_source_values()
    {
        RetryPolicy policy = RetryPolicy.Default;

        Assert.Equal(2, policy.MaxRetries);
        Assert.Equal(500, policy.BackoffInitialMs);
        Assert.Equal(5_000, policy.BackoffMaxMs);
        Assert.Equal(0.25, policy.BackoffJitter);
        Assert.True(policy.RespectRetryAfter);
        Assert.Equal(60_000, policy.MaxRetryAfterMs);
        Assert.True(policy.ApiConnectionError);
        Assert.True(policy.ApiTimeoutError);
        Assert.Equal(ExpectedDefaultStatuses, policy.HttpStatuses.OrderBy(status => status));
    }

    [Fact]
    public void RT02_status_lookup_uses_the_resolved_policy_set()
    {
        Assert.True(RetryHelpers.IsRetryableStatus(503));
        Assert.False(RetryHelpers.IsRetryableStatus(400));
        RetryPolicy policy = RetryPolicy.Default.Resolve(new RetryOptions
        {
            HttpStatuses = new HashSet<int> { 409 },
        });

        Assert.True(RetryHelpers.IsRetryableStatus(409, policy));
        Assert.False(RetryHelpers.IsRetryableStatus(503, policy));
    }

    [Fact]
    public void RT03_retry_after_parsing_is_invariant_and_prefers_milliseconds()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var now = new DateTimeOffset(2026, 10, 21, 7, 28, 0, TimeSpan.Zero);

            Assert.Equal(3_000, RetryHelpers.ParseRetryAfter(Headers(("Retry-After", "3")), now));
            Assert.Equal(1_500, RetryHelpers.ParseRetryAfter(Headers(("Retry-After", "1.5")), now));
            Assert.Equal(250, RetryHelpers.ParseRetryAfter(
                Headers(("retry-after-ms", "250"), ("retry-after", "3")), now));
            Assert.Equal(5_000, RetryHelpers.ParseRetryAfter(
                Headers(("retry-after", "Wed, 21 Oct 2026 07:28:05 GMT")), now));
            Assert.Equal(0, RetryHelpers.ParseRetryAfter(
                Headers(("retry-after", "Wed, 21 Oct 2026 07:27:00 GMT")), now));
            Assert.Null(RetryHelpers.ParseRetryAfter(Headers(("retry-after", "-5")), now));
            Assert.Null(RetryHelpers.ParseRetryAfter(Headers(("retry-after-ms", "nope")), now));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void RT04_and_RT05_backoff_caps_and_uses_js_rounding()
    {
        RetryPolicy policy = RetryPolicy.Default;

        Assert.Equal(ExpectedBackoffDelays,
            Enumerable.Range(0, 6).Select(attempt =>
                RetryHelpers.CalculateDelayMs(attempt, null, policy, () => 0)).ToArray());
        Assert.Equal(375, RetryHelpers.CalculateDelayMs(0, null, policy, () => 1));
        Assert.Equal(875, RetryHelpers.CalculateDelayMs(1, null, policy, () => 0.5));
    }

    [Fact]
    public async Task RT06_to_RT08_server_delay_ceiling_and_cancellation_are_preserved()
    {
        RetryPolicy policy = RetryPolicy.Default;
        Assert.Equal(2_000, RetryHelpers.CalculateDelayMs(
            0, Headers(("retry-after", "2")), policy, () => 1));
        Assert.Equal(60_000, RetryHelpers.CalculateDelayMs(
            0, Headers(("retry-after-ms", "60000")), policy, () => 1));
        Assert.Equal(375, RetryHelpers.CalculateDelayMs(
            0, Headers(("retry-after-ms", "60001")), policy, () => 1));
        Assert.Equal(500, RetryHelpers.CalculateDelayMs(
            0, Headers(("retry-after", "2")), new RetryPolicy { RespectRetryAfter = false }, () => 0));

        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => RetryHelpers.SleepAsync(10_000, source.Token));
    }

    [Fact]
    public void RT10_retry_policy_validation_rejects_invalid_values_and_copies_statuses()
    {
        Assert.Throws<TypeSafeException>(() => RetryPolicy.Default.Resolve(new RetryOptions { MaxRetries = -1 }));
        Assert.Throws<TypeSafeException>(() => RetryPolicy.Default.Resolve(new RetryOptions { BackoffJitter = 1.1 }));
        Assert.Throws<TypeSafeException>(() => RetryPolicy.Default.Resolve(new RetryOptions
        {
            HttpStatuses = new HashSet<int> { 99 },
        }));

        var statuses = new HashSet<int> { 409 };
        RetryPolicy resolved = RetryPolicy.Default.Resolve(new RetryOptions { HttpStatuses = statuses });
        statuses.Add(503);
        Assert.DoesNotContain(503, resolved.HttpStatuses);
    }

    [Theory]
    [InlineData(400, typeof(BadRequestException))]
    [InlineData(401, typeof(AuthenticationException))]
    [InlineData(403, typeof(PermissionDeniedException))]
    [InlineData(404, typeof(NotFoundException))]
    [InlineData(422, typeof(UnprocessableEntityException))]
    [InlineData(429, typeof(RateLimitException))]
    [InlineData(500, typeof(InternalServerException))]
    [InlineData(503, typeof(InternalServerException))]
    [InlineData(418, typeof(ApiException))]
    public void ER01_http_statuses_map_to_the_expected_exception_type(int status, Type type)
    {
        ApiException exception = ApiException.FromResponse(status, null, Headers());

        Assert.IsType(type, exception);
        Assert.IsAssignableFrom<TypeSafeException>(exception);
        Assert.Equal(status, exception.Status);
    }

    [Fact]
    public void ER02_to_ER07_extract_structured_messages_request_ids_and_validation_paths()
    {
        var headers = Headers(("X-TypeSafe-Request-Id", "req_123"));
        var nested = JsonNode.Parse("{\"error\":{\"message\":\"invalid api key\"}}")!;
        ApiException nestedError = ApiException.FromResponse(401, nested, headers);

        Assert.Equal("401 invalid api key", nestedError.Message);
        Assert.Equal("req_123", nestedError.RequestId);
        Assert.Same(nested, nestedError.Body);
        Assert.Equal("req_123", nestedError.Headers["x-typesafe-request-id"]);

        string validationJson = new JsonObject
        {
            ["detail"] = new JsonArray
            {
                new JsonObject
                {
                    ["loc"] = new JsonArray("body", "questions", "q", "score", "criteria"),
                    ["msg"] = "Input should be a valid list",
                },
                new JsonObject
                {
                    ["loc"] = new JsonArray("body", "questions"),
                    ["msg"] = "Dictionary should have at least 1 item",
                },
            },
        }.ToJsonString();
        var bodies = new (string Json, string Message)[]
        {
            ("{\"error\":\"plain string\"}", "plain string"),
            ("{\"message\":\"top-level message\"}", "top-level message"),
            ("{\"detail\":\"fastapi style\"}", "fastapi style"),
            ("{\"detail\":{\"message\":\"Unknown model: x\"}}", "Unknown model: x"),
            (validationJson, "questions.q.score.criteria: Input should be a valid list; questions: Dictionary should have at least 1 item"),
        };

        foreach ((string json, string message) in bodies)
        {
            ApiException exception = ApiException.FromResponse(400, JsonNode.Parse(json), Headers());
            Assert.Equal($"400 {message}", exception.Message);
        }
    }

    [Fact]
    public void ER08_to_ER11_use_raw_truncation_text_empty_and_content_type_independent_json()
    {
        ApiException shortBody = ApiException.FromResponse(
            400, JsonNode.Parse("{\"code\":7}"), Headers());
        Assert.Equal("400 {\"code\":7}", shortBody.Message);

        ApiException longBody = ApiException.FromResponse(
            400, JsonNode.Parse("{\"blob\":\"" + new string('x', 500) + "\"}"), Headers());
        Assert.Equal(205, longBody.Message.Length);
        Assert.EndsWith("…", longBody.Message, StringComparison.Ordinal);

        ApiException text = ApiException.FromResponse(502, "<h1>bad gateway</h1>", Headers());
        Assert.Equal("502 <h1>bad gateway</h1>", text.Message);

        ApiException empty = ApiException.FromResponse(429, null, Headers());
        Assert.Equal("429 status code (no body)", empty.Message);

        ParsedErrorBody parsed = ErrorBodyParser.Parse("{\"message\":\"no content type\"}");
        ApiException noContentType = ApiException.FromResponse(400, parsed.Value, Headers(), parsed.HasBody);
        Assert.Equal("400 no content type", noContentType.Message);

        ParsedErrorBody explicitNull = ErrorBodyParser.Parse("null");
        ApiException nullBody = ApiException.FromResponse(400, explicitNull.Value, Headers(), explicitNull.HasBody);
        Assert.Equal("400 null", nullBody.Message);
    }

    [Fact]
    public void CL02_and_CL05_environment_values_are_trimmed_and_blank_values_are_missing()
    {
        const string name = "TYPESAFE_TEST_VALUE";
        string? original = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, "  value  ");
            Assert.Equal("value", TypeSafeEnvironment.Read(name));
            Environment.SetEnvironmentVariable(name, "  ");
            Assert.Null(TypeSafeEnvironment.Read(name));
            Assert.Equal("explicit", TypeSafeEnvironment.FromCodeOrEnvironment("explicit", name));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, original);
        }
    }

    [Fact]
    public void Api_connection_and_timeout_exceptions_preserve_causes_and_timeout()
    {
        var cause = new InvalidOperationException("socket failed");
        var connection = new ApiConnectionException("Connection error.", cause);
        var timeout = new ApiTimeoutException(1_000, cause);

        Assert.Same(cause, connection.InnerException);
        Assert.Same(cause, timeout.InnerException);
        Assert.Equal(1_000, timeout.TimeoutMs);
        Assert.Equal("Request timed out after 1000ms.", timeout.Message);
        Assert.IsAssignableFrom<ApiConnectionException>(timeout);
        Assert.IsAssignableFrom<TypeSafeException>(new ApiUserAbortException());
    }

    private static Dictionary<string, string> Headers(params (string Name, string Value)[] values) =>
        values.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
}
#pragma warning restore CA1707
