using System.Text.Json;
using System.Text.Json.Nodes;
using TypeSafe.Ai.Json;

namespace TypeSafe.Ai.Tests;

#pragma warning disable CA1707
public sealed class JsonContractTests
{
    [Fact]
    public void Noul_without_criteria_writes_explicit_null_instructions_and_omits_criteria()
    {
        NoulQuestion question = Questions.Noul();

        using JsonDocument document = SerializeQuestion(question);
        JsonElement root = document.RootElement;

        Assert.Equal(JsonValueKind.Null, root.GetProperty("instructions").ValueKind);
        Assert.False(root.TryGetProperty("criteria", out _));
    }

    [Fact]
    public void Noul_with_explicit_null_criteria_writes_both_null_properties()
    {
        NoulQuestion question = Questions.Noul(null, null);

        using JsonDocument document = SerializeQuestion(question);
        JsonElement root = document.RootElement;

        Assert.Equal(JsonValueKind.Null, root.GetProperty("instructions").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("criteria").ValueKind);
    }

    [Fact]
    public void Noul_criteria_tracks_explicit_null_members()
    {
        NoulQuestion question = Questions.Noul(
            "question",
            new NoulCriteria { True = null, False = EntryValue.FromString("no") });

        using JsonDocument document = SerializeQuestion(question);
        JsonElement criteria = document.RootElement.GetProperty("criteria");

        Assert.Equal(JsonValueKind.Null, criteria.GetProperty("true").ValueKind);
        Assert.Equal("no", criteria.GetProperty("false").GetString());
    }

    [Fact]
    public void EntryValue_rejects_top_level_numbers_and_booleans_but_preserves_nested_values()
    {
        Assert.Throws<TypeSafeException>(() => EntryValue.Parse("42"));
        Assert.Throws<TypeSafeException>(() => EntryValue.Parse("true"));

        EntryValue value = EntryValue.FromObject(new JsonObject
        {
            ["number"] = 42,
            ["boolean"] = true,
            ["nested"] = new JsonArray(1, false),
        });

        Assert.Equal(EntryValueKind.ObjectValue, value.Kind);
        Assert.Contains("42", value.ToString(), StringComparison.Ordinal);
        Assert.Contains("true", value.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Choice_and_score_questions_preserve_rich_json_descriptions()
    {
        var rich = EntryValue.FromObject(new JsonObject
        {
            ["summary"] = "warm",
            ["examples"] = new JsonArray("hello", "welcome"),
        });
        ChoiceQuestion choice = Questions.Choice("which", new Dictionary<string, EntryValue?>
        {
            ["friendly"] = rich,
            ["hostile"] = null,
        });
        ScoreQuestion score = Questions.Score("how", new EntryValue?[] { rich, "meh" });

        string choiceJson = JsonSerializer.Serialize<Question>(choice, TypeSafeJson.Options);
        string scoreJson = JsonSerializer.Serialize<Question>(score, TypeSafeJson.Options);

        Assert.Contains("friendly", choiceJson, StringComparison.Ordinal);
        Assert.Contains("\"hostile\":null", choiceJson.Replace(" ", string.Empty), StringComparison.Ordinal);
        Assert.Contains("[", scoreJson, StringComparison.Ordinal);
        Assert.Contains("summary", scoreJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_question_shapes_are_rejected_by_the_wire_converter()
    {
        Assert.Throws<TypeSafeException>(() => DeserializeQuestion(
            "{\"type\":\"choice\",\"criteria\":[\"a\",\"b\"]}"));
        Assert.Throws<TypeSafeException>(() => DeserializeQuestion(
            "{\"type\":\"score\",\"criteria\":{\"0\":\"bad\",\"1\":\"good\"}}"));
        Assert.Throws<TypeSafeException>(() => DeserializeQuestion(
            "{\"type\":\"score\",\"criteria\":[\"only\"]}"));
    }

    [Fact]
    public void Models_and_usage_use_the_source_snake_case_names()
    {
        ModelCard model = JsonSerializer.Deserialize<ModelCard>(
            "{\"name\":\"m\",\"description\":\"d\",\"release_date\":\"arbitrary\"}",
            TypeSafeJson.Options)!;
        Usage usage = JsonSerializer.Deserialize<Usage>(
            "{\"input_tokens\":4,\"output_tokens\":2}", TypeSafeJson.Options)!;

        Assert.Equal("arbitrary", model.ReleaseDate);
        Assert.Equal(4, usage.InputTokens);
        Assert.Equal(2, usage.OutputTokens);
    }

    [Fact]
    public void Answers_preserve_discriminators_and_arbitrary_question_names()
    {
        const string json = """
            {
              "model":"m",
              "answers":{
                "__proto__":{"type":"noul","noul":0.75},
                "tone":{"type":"choice","choice":"warm","confidence":0.9,"probabilities":{"warm":0.9}}
              },
              "usage":{"input_tokens":1,"output_tokens":2}
            }
            """;

        SystemOneResult result = JsonSerializer.Deserialize<SystemOneResult>(json, TypeSafeJson.Options)!;

        Assert.IsType<NoulAnswer>(result.Answers["__proto__"]);
        Assert.Equal("warm", Assert.IsType<ChoiceAnswer>(result.Answers["tone"]).Choice);
    }

    [Fact]
    public void Request_extension_nulls_are_not_dropped()
    {
        var request = new SystemOneRequest
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
        };

        using JsonDocument document = JsonDocument.Parse(
            JsonSerializer.Serialize(request, TypeSafeJson.Options));

        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("state").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("future_option").ValueKind);
    }

    private static JsonDocument SerializeQuestion(Question question) =>
        JsonDocument.Parse(JsonSerializer.Serialize(question, TypeSafeJson.Options));

    private static Question DeserializeQuestion(string json) =>
        JsonSerializer.Deserialize<Question>(json, TypeSafeJson.Options)!;
}
#pragma warning restore CA1707
