namespace TypeSafe.Ai.IntegrationTests;

#pragma warning disable CA1707
[Trait("Category", "Integration")]
public sealed class LiveApiTests
{
    private static readonly string[] AnswerTypes = { "noul", "choice", "score" };

    [Fact(Timeout = 120_000)]
    public async Task IN01_models_can_be_listed_when_a_key_is_configured()
    {
        if (!HasKey()) return;
        using TypeSafeClient client = CreateClient();
        IReadOnlyList<ModelCard> models = await client.Models.ListAsync();
        Assert.NotNull(models);
    }

    [Fact(Timeout = 120_000)]
    public async Task IN02_system_one_accepts_all_question_discriminators()
    {
        if (!HasKey()) return;
        using TypeSafeClient client = CreateClient();
        SystemOneResult result = await client.SystemOneAsync(new SystemOneRequest
        {
            State = "A customer asks for help with a payment.",
            Questions = new Dictionary<string, Question>
            {
                ["billing"] = Questions.Noul("Is this billing?"),
                ["tone"] = Questions.Choice("What is the tone?", new Dictionary<string, EntryValue?>
                {
                    ["calm"] = null,
                    ["urgent"] = "Urgent wording",
                }),
                ["urgency"] = Questions.Score("How urgent?", new EntryValue?[]
                {
                    "later", "soon", "now",
                }),
            },
        });

        Assert.Equal(3, result.Answers.Count);
        Assert.All(result.Answers.Values, answer => Assert.Contains(answer.Type, AnswerTypes));
    }

    [Fact(Timeout = 120_000)]
    public async Task IN03_rich_descriptions_and_one_sided_criteria_round_trip()
    {
        if (!HasKey()) return;
        using TypeSafeClient client = CreateClient();
        SystemOneResult result = await client.SystemOneAsync(new SystemOneRequest
        {
            State = EntryValue.FromObject(new System.Text.Json.Nodes.JsonObject
            {
                ["subject"] = "Payment question",
                ["messages"] = new System.Text.Json.Nodes.JsonArray("Please help."),
            }),
            Questions = new Dictionary<string, Question>
            {
                ["oneSided"] = Questions.Noul("Is this a payment issue?", new NoulCriteria
                {
                    True = EntryValue.FromObject(new System.Text.Json.Nodes.JsonObject
                    {
                        ["examples"] = new System.Text.Json.Nodes.JsonArray("charged twice"),
                    }),
                }),
                ["choice"] = Questions.Choice("Choose a label", new Dictionary<string, EntryValue?>
                {
                    ["payment"] = null,
                    ["other"] = "Not a payment issue",
                }),
            },
        });

        Assert.Equal(2, result.Answers.Count);
    }

    [Fact(Timeout = 120_000)]
    public async Task IN05_unknown_model_is_a_bad_request()
    {
        if (!HasKey()) return;
        using TypeSafeClient client = new(new TypeSafeClientOptions
        {
            Retry = new RetryPolicy { MaxRetries = 0 },
        });

        await Assert.ThrowsAsync<BadRequestException>(async () => await client.SystemOneAsync(new SystemOneRequest
        {
            State = "state",
            Model = "typesafe-model-that-does-not-exist",
            Questions = new Dictionary<string, Question>
            {
                ["q"] = Questions.Noul("question"),
            },
        }));
    }

    [Fact(Timeout = 120_000)]
    public async Task IN07_empty_questions_are_rejected_client_side()
    {
        using TypeSafeClient client = new(new TypeSafeClientOptions
        {
            ApiKey = "local-validation-key",
        });

        await Assert.ThrowsAsync<TypeSafeException>(async () => await client.SystemOneAsync(new SystemOneRequest
        {
            State = "state",
            Questions = new Dictionary<string, Question>(),
        }));
    }

    [Fact(Timeout = 120_000)]
    public async Task IN04_invalid_keys_map_to_authentication_errors()
    {
        string? key = Environment.GetEnvironmentVariable(TypeSafeEnvironment.ApiKey);
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        using var client = new TypeSafeClient(new TypeSafeClientOptions
        {
            ApiKey = $"{key}-invalid",
            Retry = new RetryPolicy { MaxRetries = 0 },
        });
        await Assert.ThrowsAsync<AuthenticationException>(async () => await client.Models.ListAsync());
    }

    private static TypeSafeClient CreateClient()
    {
        return new TypeSafeClient(new TypeSafeClientOptions
        {
            Timeout = TimeSpan.FromSeconds(120),
        });
    }

    private static bool HasKey() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TypeSafeEnvironment.ApiKey));

}
#pragma warning restore CA1707
