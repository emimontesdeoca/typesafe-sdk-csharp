using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TypeSafe.Ai.Json;

internal static class TypeSafeJson
{
    internal static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new EntryValueJsonConverter());
        options.Converters.Add(new QuestionJsonConverter());
        options.Converters.Add(new AnswerJsonConverter());
        options.Converters.Add(new SystemOneRequestJsonConverter());
        options.Converters.Add(new SystemOneResultJsonConverter());
        return options;
    }
}

internal sealed class EntryValueJsonConverter : JsonConverter<EntryValue>
{
    public override bool HandleNull => true;

    public override EntryValue? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        return EntryValue.FromJsonElement(document.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, EntryValue value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        value.WriteTo(writer, options);
    }
}

internal sealed class QuestionJsonConverter : JsonConverter<Question>
{
    public override bool HandleNull => true;

    public override Question Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            throw new TypeSafeException("Questions must be JSON objects.");
        }

        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new TypeSafeException("Questions must be JSON objects.");
        }

        string type = RequiredString(root, "type", "Question type is required.");
        return type switch
        {
            "noul" => ReadNoul(root),
            "choice" => ReadChoice(root),
            "score" => ReadScore(root),
            _ => throw new TypeSafeException($"Unknown question type \"{type}\"."),
        };
    }

    public override void Write(Utf8JsonWriter writer, Question value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("type", value.Type);

        switch (value)
        {
            case NoulQuestion noul:
                if (noul.InstructionsSpecified)
                {
                    writer.WritePropertyName("instructions");
                    WriteEntry(writer, noul.Instructions, options);
                }

                if (noul.CriteriaSpecified)
                {
                    writer.WritePropertyName("criteria");
                    WriteNoulCriteria(writer, noul.Criteria, options);
                }

                break;
            case ChoiceQuestion choice:
                if (choice.InstructionsSpecified)
                {
                    writer.WritePropertyName("instructions");
                    WriteEntry(writer, choice.Instructions, options);
                }

                writer.WritePropertyName("criteria");
                WriteChoiceCriteria(writer, choice.Criteria, options);
                break;
            case ScoreQuestion score:
                if (score.InstructionsSpecified)
                {
                    writer.WritePropertyName("instructions");
                    WriteEntry(writer, score.Instructions, options);
                }

                writer.WritePropertyName("criteria");
                writer.WriteStartArray();
                foreach (EntryValue? entry in score.Criteria)
                {
                    WriteEntry(writer, entry, options);
                }

                writer.WriteEndArray();
                break;
            default:
                throw new TypeSafeException($"Unsupported question type {value.GetType().Name}.");
        }

        writer.WriteEndObject();
    }

    private static NoulQuestion ReadNoul(JsonElement root)
    {
        var question = new NoulQuestion();
        if (root.TryGetProperty("instructions", out JsonElement instructions))
        {
            question.SetInstructions(ReadNullableEntry(instructions));
        }

        if (root.TryGetProperty("criteria", out JsonElement criteria))
        {
            question.SetCriteria(ReadNoulCriteria(criteria));
        }

        return question;
    }

    private static ChoiceQuestion ReadChoice(JsonElement root)
    {
        var question = new ChoiceQuestion();
        if (root.TryGetProperty("instructions", out JsonElement instructions))
        {
            question.SetInstructions(ReadNullableEntry(instructions));
        }

        if (!root.TryGetProperty("criteria", out JsonElement criteria) ||
            criteria.ValueKind != JsonValueKind.Object)
        {
            throw new TypeSafeException(
                "Choice criteria must be a map of labels to descriptions, not a list.");
        }

        question.SetCriteria(ReadEntryMap(criteria));
        return question;
    }

    private static ScoreQuestion ReadScore(JsonElement root)
    {
        var question = new ScoreQuestion();
        if (root.TryGetProperty("instructions", out JsonElement instructions))
        {
            question.SetInstructions(ReadNullableEntry(instructions));
        }

        if (!root.TryGetProperty("criteria", out JsonElement criteria) ||
            criteria.ValueKind != JsonValueKind.Array)
        {
            throw new TypeSafeException(
                "Score criteria must be a list of descriptions indexed by score from zero, not a map.");
        }

        List<EntryValue?> entries = criteria.EnumerateArray().Select(ReadNullableEntry).ToList();
        if (entries.Count < 2)
        {
            throw new TypeSafeException("Score criteria must contain at least two descriptions indexed by score from zero.");
        }

        question.SetCriteria(entries);
        return question;
    }

    private static NoulCriteria? ReadNoulCriteria(JsonElement criteria)
    {
        if (criteria.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (criteria.ValueKind != JsonValueKind.Object)
        {
            throw new TypeSafeException("Noul criteria must be an object or null.");
        }

        var result = new NoulCriteria();
        if (criteria.TryGetProperty("true", out JsonElement trueValue))
        {
            result.SetTrue(ReadNullableEntry(trueValue));
        }

        if (criteria.TryGetProperty("false", out JsonElement falseValue))
        {
            result.SetFalse(ReadNullableEntry(falseValue));
        }

        return result;
    }

    private static Dictionary<string, EntryValue?> ReadEntryMap(JsonElement element)
    {
        var result = new Dictionary<string, EntryValue?>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            result[property.Name] = ReadNullableEntry(property.Value);
        }

        return result;
    }

    private static void WriteNoulCriteria(
        Utf8JsonWriter writer,
        NoulCriteria? criteria,
        JsonSerializerOptions options)
    {
        if (criteria is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        if (criteria.TrueSpecified)
        {
            writer.WritePropertyName("true");
            WriteEntry(writer, criteria.True, options);
        }

        if (criteria.FalseSpecified)
        {
            writer.WritePropertyName("false");
            WriteEntry(writer, criteria.False, options);
        }

        writer.WriteEndObject();
    }

    private static void WriteChoiceCriteria(
        Utf8JsonWriter writer,
        IReadOnlyDictionary<string, EntryValue?> criteria,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach ((string label, EntryValue? description) in criteria)
        {
            writer.WritePropertyName(label);
            WriteEntry(writer, description, options);
        }

        writer.WriteEndObject();
    }

    private static void WriteEntry(Utf8JsonWriter writer, EntryValue? entry, JsonSerializerOptions options)
    {
        if (entry is null)
        {
            writer.WriteNullValue();
            return;
        }

        entry.WriteTo(writer, options);
    }

    private static EntryValue? ReadNullableEntry(JsonElement element) =>
        element.ValueKind == JsonValueKind.Null ? null : EntryValue.FromJsonElement(element);

    private static string RequiredString(JsonElement root, string name, string message)
    {
        if (!root.TryGetProperty(name, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            throw new TypeSafeException(message);
        }

        return property.GetString()!;
    }
}

internal sealed class AnswerJsonConverter : JsonConverter<Answer>
{
    public override bool HandleNull => true;

    public override Answer Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            throw new TypeSafeException("Answers must be JSON objects.");
        }

        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new TypeSafeException("Answers must be JSON objects.");
        }

        string type = RequiredString(root, "type", "Answer type is required.");
        return type switch
        {
            "noul" => new NoulAnswer { Noul = RequiredDouble(root, "noul") },
            "choice" => ReadChoice(root),
            "score" => ReadScore(root),
            _ => throw new TypeSafeException($"Unknown answer type \"{type}\"."),
        };
    }

    public override void Write(Utf8JsonWriter writer, Answer value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("type", value.Type);
        switch (value)
        {
            case NoulAnswer noul:
                writer.WriteNumber("noul", noul.Noul);
                break;
            case ChoiceAnswer choice:
                writer.WriteString("choice", choice.Choice);
                writer.WriteNumber("confidence", choice.Confidence);
                WriteNumberMap(writer, "probabilities", choice.Probabilities);
                break;
            case ScoreAnswer score:
                writer.WriteNumber("score", score.Score);
                writer.WriteNumber("confidence", score.Confidence);
                WriteEntryMap(writer, "legend", score.Legend, options);
                WriteIntegerNumberMap(writer, "probabilities", score.Probabilities);
                break;
            default:
                throw new TypeSafeException($"Unsupported answer type {value.GetType().Name}.");
        }

        writer.WriteEndObject();
    }

    private static ChoiceAnswer ReadChoice(JsonElement root)
    {
        string choice = RequiredString(root, "choice", "Choice answer is missing its label.");
        return new ChoiceAnswer
        {
            Choice = choice,
            Confidence = RequiredDouble(root, "confidence"),
            Probabilities = ReadNumberMap(root, "probabilities"),
        };
    }

    private static ScoreAnswer ReadScore(JsonElement root)
    {
        return new ScoreAnswer
        {
            Score = RequiredDouble(root, "score"),
            Confidence = RequiredDouble(root, "confidence"),
            Legend = ReadEntryNumberMap(root, "legend"),
            Probabilities = ReadNumberMap(root, "probabilities")
                .ToDictionary(pair => int.Parse(pair.Key, CultureInfo.InvariantCulture), pair => pair.Value),
        };
    }

    private static Dictionary<string, double> ReadNumberMap(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement property) ||
            property.ValueKind != JsonValueKind.Object)
        {
            throw new TypeSafeException($"Answer property \"{name}\" must be an object.");
        }

        return property.EnumerateObject().ToDictionary(
            item => item.Name,
            item => item.Value.GetDouble(),
            StringComparer.Ordinal);
    }

    private static Dictionary<int, EntryValue?> ReadEntryNumberMap(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement property) ||
            property.ValueKind != JsonValueKind.Object)
        {
            throw new TypeSafeException($"Answer property \"{name}\" must be an object.");
        }

        var result = new Dictionary<int, EntryValue?>();
        foreach (JsonProperty item in property.EnumerateObject())
        {
            if (!int.TryParse(item.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int key))
            {
                throw new TypeSafeException($"Score legend key \"{item.Name}\" is not an integer.");
            }

            result[key] = item.Value.ValueKind == JsonValueKind.Null
                ? null
                : EntryValue.FromJsonElement(item.Value);
        }

        return result;
    }

    private static double RequiredDouble(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement property) ||
            property.ValueKind != JsonValueKind.Number || !property.TryGetDouble(out double value))
        {
            throw new TypeSafeException($"Answer property \"{name}\" must be a number.");
        }

        return value;
    }

    private static string RequiredString(JsonElement root, string name, string message)
    {
        if (!root.TryGetProperty(name, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            throw new TypeSafeException(message);
        }

        return property.GetString()!;
    }

    private static void WriteNumberMap(
        Utf8JsonWriter writer,
        string name,
        IReadOnlyDictionary<string, double> values)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        foreach ((string key, double value) in values)
        {
            writer.WriteNumber(key, value);
        }

        writer.WriteEndObject();
    }

    private static void WriteIntegerNumberMap(
        Utf8JsonWriter writer,
        string name,
        IReadOnlyDictionary<int, double> values)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        foreach ((int key, double value) in values)
        {
            writer.WriteNumber(key.ToString(CultureInfo.InvariantCulture), value);
        }

        writer.WriteEndObject();
    }

    private static void WriteEntryMap(
        Utf8JsonWriter writer,
        string name,
        IReadOnlyDictionary<int, EntryValue?> values,
        JsonSerializerOptions options)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        foreach ((int key, EntryValue? value) in values)
        {
            writer.WritePropertyName(key.ToString(CultureInfo.InvariantCulture));
            if (value is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                value.WriteTo(writer, options);
            }
        }

        writer.WriteEndObject();
    }
}

internal sealed class SystemOneRequestJsonConverter : JsonConverter<SystemOneRequest>
{
    public override bool HandleNull => true;

    public override SystemOneRequest Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            throw new TypeSafeException("System-one requests must be JSON objects.");
        }

        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new TypeSafeException("System-one requests must be JSON objects.");
        }

        var extension = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        EntryValue? state = null;
        IReadOnlyDictionary<string, Question> questions =
            new Dictionary<string, Question>(StringComparer.Ordinal);
        string? model = null;

        foreach (JsonProperty property in root.EnumerateObject())
        {
            switch (property.Name)
            {
                case "state":
                    state = property.Value.ValueKind == JsonValueKind.Null
                        ? null
                        : EntryValue.FromJsonElement(property.Value);
                    break;
                case "questions":
                    questions = JsonSerializer.Deserialize<Dictionary<string, Question>>(
                        property.Value.GetRawText(), options) ??
                        new Dictionary<string, Question>(StringComparer.Ordinal);
                    break;
                case "model":
                    model = property.Value.ValueKind == JsonValueKind.Null
                        ? null
                        : property.Value.GetString();
                    break;
                default:
                    extension[property.Name] = property.Value.ValueKind == JsonValueKind.Null
                        ? null
                        : JsonNode.Parse(property.Value.GetRawText());
                    break;
            }
        }

        var result = new SystemOneRequest
        {
            State = state,
            Questions = questions,
            ExtensionData = extension,
        };
        if (root.TryGetProperty("model", out _))
        {
            result.SetModel(model);
        }

        return result;
    }

    public override void Write(
        Utf8JsonWriter writer,
        SystemOneRequest value,
        JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName("state");
        if (value.State is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            value.State.WriteTo(writer, options);
        }

        writer.WritePropertyName("questions");
        writer.WriteStartObject();
        foreach ((string name, Question question) in value.Questions)
        {
            writer.WritePropertyName(name);
            JsonSerializer.Serialize(writer, question, options);
        }

        writer.WriteEndObject();
        if (value.ModelSpecified)
        {
            writer.WritePropertyName("model");
            if (value.Model is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStringValue(value.Model);
            }
        }

        foreach ((string name, JsonNode? extension) in value.ExtensionData)
        {
            writer.WritePropertyName(name);
            extension?.WriteTo(writer, options);
            if (extension is null)
            {
                writer.WriteNullValue();
            }
        }

        writer.WriteEndObject();
    }
}

internal sealed class SystemOneResultJsonConverter : JsonConverter<SystemOneResult>
{
    public override bool HandleNull => true;

    public override SystemOneResult Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            throw new TypeSafeException("System-one responses must be JSON objects.");
        }

        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new TypeSafeException("System-one responses must be JSON objects.");
        }

        string model = root.TryGetProperty("model", out JsonElement modelValue)
            ? modelValue.GetString() ?? string.Empty
            : string.Empty;
        var answers = new Dictionary<string, Answer>(StringComparer.Ordinal);
        if (root.TryGetProperty("answers", out JsonElement answerValue) &&
            answerValue.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in answerValue.EnumerateObject())
            {
                Answer answer = JsonSerializer.Deserialize<Answer>(property.Value.GetRawText(), options)
                    ?? throw new TypeSafeException("Answer cannot be null.");
                answers[property.Name] = answer;
            }
        }

        Usage usage = root.TryGetProperty("usage", out JsonElement usageValue)
            ? JsonSerializer.Deserialize<Usage>(usageValue.GetRawText(), options) ?? new Usage()
            : new Usage();
        return new SystemOneResult { Model = model, Answers = answers, Usage = usage };
    }

    public override void Write(
        Utf8JsonWriter writer,
        SystemOneResult value,
        JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("model", value.Model);
        writer.WritePropertyName("answers");
        writer.WriteStartObject();
        foreach ((string name, Answer answer) in value.Answers)
        {
            writer.WritePropertyName(name);
            JsonSerializer.Serialize(writer, answer, options);
        }

        writer.WriteEndObject();
        writer.WritePropertyName("usage");
        writer.WriteStartObject();
        writer.WriteNumber("input_tokens", value.Usage.InputTokens);
        writer.WriteNumber("output_tokens", value.Usage.OutputTokens);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}
