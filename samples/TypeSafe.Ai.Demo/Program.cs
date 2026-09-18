using System.Text.Json.Nodes;
using TypeSafe.Ai;

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TypeSafeEnvironment.ApiKey)))
{
    Console.WriteLine($"Set {TypeSafeEnvironment.ApiKey} before running this sample.");
    return;
}

using var client = new TypeSafeClient(new TypeSafeClientOptions
{
    Timeout = TimeSpan.FromSeconds(10),
    Retry = new RetryPolicy { MaxRetries = 2 },
});

try
{
    IReadOnlyList<ModelCard> models = await client.Models.ListAsync();
    Console.WriteLine($"Available models: {models.Count}");

    var questions = new Dictionary<string, Question>
    {
        ["billing"] = Questions.Noul(
            "Is this about billing?",
            new NoulCriteria { True = "A payment issue", False = null }),
        ["tone"] = Questions.Choice("What is the tone?", new Dictionary<string, EntryValue?>
        {
            ["calm"] = null,
            ["frustrated"] = "Urgent or unhappy wording",
        }),
        ["urgency"] = Questions.Score("How urgent is this?", new EntryValue?[]
        {
            "can wait", "this week", "today",
        }),
    };
    var state = EntryValue.FromObject(new JsonObject
    {
        ["subject"] = "Charged twice",
        ["messages"] = new JsonArray("Please help resolve this."),
    });

    using WithResponse<SystemOneResult> response = await client.SystemOneAsync(
        new SystemOneRequest { State = state, Questions = questions }).WithResponseAsync();
    Console.WriteLine($"Model: {response.Data.Model}; request: {response.RequestId ?? "none"}");
}
catch (ApiException exception)
{
    Console.Error.WriteLine($"TypeSafe API error {exception.Status}: {exception.Message}");
}
