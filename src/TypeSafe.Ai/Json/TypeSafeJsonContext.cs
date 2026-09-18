using System.Text.Json.Serialization;

namespace TypeSafe.Ai.Json;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Answer))]
[JsonSerializable(typeof(Dictionary<string, Answer>))]
[JsonSerializable(typeof(Dictionary<string, Question>))]
[JsonSerializable(typeof(ModelCard))]
[JsonSerializable(typeof(SystemOneRequest))]
[JsonSerializable(typeof(SystemOneResult))]
[JsonSerializable(typeof(Usage))]
internal partial class TypeSafeJsonContext : JsonSerializerContext
{
}
