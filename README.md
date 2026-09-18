# TypeSafe.Ai

The TypeSafe .NET SDK for `net10.0`. It provides typed models, question builders, retry behavior,
buffered raw responses, request IDs, and the same JSON contract as `@typesafe-ai/sdk` v0.6.0.

## Install

```bash
dotnet add package TypeSafe.Ai --version 0.6.0
```

The package targets .NET 10 only. Supply `TYPESAFE_API_KEY` through the host environment, or pass
`ApiKey` in `TypeSafeClientOptions`. The key is private client state, is ignored by options JSON
serialization, and is never written to SDK logs.

## Minimal usage

```csharp
using TypeSafe.Ai;

using var client = new TypeSafeClient();
IReadOnlyList<ModelCard> models = await client.Models.ListAsync();

SystemOneResult result = await client.SystemOneAsync(new SystemOneRequest
{
	State = "I was charged twice.",
	Questions = new Dictionary<string, Question>
	{
		["isBilling"] = Questions.Noul("Is this about billing?"),
	},
});

double probability = ((NoulAnswer)result.Answers["isBilling"]).Noul;
```

## Configuration and responses

`BaseUri`, `DefaultModel`, `Timeout`, `Retry`, `DefaultHeaders`, `LogLevel`, and an injected
`HttpClient` are configured through `TypeSafeClientOptions`. Timeout is per attempt; retries and
backoff can make a complete operation last longer. The SDK buffers each body before exposing it.
There is no separate dependency-injection package: an `IHttpClientFactory`-managed `HttpClient` can
be passed directly, and the SDK takes ownership of the per-attempt timeout while leaving disposal
of the injected client to the caller.

`ApiPromise<T>` is awaitable and also exposes `AsResponseAsync()` and `WithResponseAsync()`. Raw
responses are caller-owned. `WithResponse<T>` is disposable and owns its `HttpResponseMessage`:

```csharp
using WithResponse<IReadOnlyList<ModelCard>> response =
	await client.Models.ListAsync().WithResponseAsync();

Console.WriteLine(response.RequestId);
string rawJson = await response.Response.Content.ReadAsStringAsync();
```

The raw and combined paths share the request with parsed and mapped consumers. Caller cancellation
is supplied to `ListAsync` or `SystemOneAsync` as a `CancellationToken`; it becomes
`ApiUserAbortException` and is never retried.

## Questions

`Questions.Noul()` sends `instructions: null` and omits `criteria`. Passing a second `null`, as in
`Questions.Noul(null, null)`, sends explicit null criteria. Choice descriptions are a dictionary;
score criteria are an ordered list with at least two entries. `EntryValue` accepts a top-level
string, JSON object, JSON array, or null, while nested JSON may contain numbers and booleans.

```csharp
var questions = new Dictionary<string, Question>
{
	["billing"] = Questions.Noul(
		"Is this billing?",
		new NoulCriteria { True = "A payment issue", False = null }),
	["tone"] = Questions.Choice("What is the tone?", new Dictionary<string, EntryValue?>
	{
		["calm"] = null,
		["frustrated"] = EntryValue.FromObject(new System.Text.Json.Nodes.JsonObject
		{
			["examples"] = new System.Text.Json.Nodes.JsonArray("ASAP"),
		}),
	}),
	["urgency"] = Questions.Score("How urgent is this?", new EntryValue?[]
	{
		"can wait", "this week", "today",
	}),
};
```

C# exposes answers as a runtime dictionary keyed by question name. When a compile-time DTO is
preferred, deserialize the answer object into a caller-owned record; C# cannot reproduce
TypeScript's literal-key inference for arbitrary dictionaries.

## Errors and logging

HTTP statuses map to `BadRequestException`, `AuthenticationException`, `PermissionDeniedException`,
`NotFoundException`, `UnprocessableEntityException`, `RateLimitException`, and
`InternalServerException`, all derived from `ApiException`. Network/body failures use
`ApiConnectionException`; SDK timeouts use `ApiTimeoutException`.

Debug logging includes request and response bodies to match the source SDK. Configure `LogLevel`
as `Warn` or `Off`, or provide an `ITypeSafeLogger`, when state or response data must not enter logs.
Known credential and cookie headers are redacted, but bodies are not generally redacted.

## Development validation

```powershell
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
dotnet pack src/TypeSafe.Ai/TypeSafe.Ai.csproj --configuration Release --output artifacts/package
dotnet test tests/TypeSafe.Ai.PackageTests --configuration Release
```

The integration project is opt-in and uses a 120-second operation timeout. Set `TYPESAFE_API_KEY`
before running it; without a key, live tests exit before constructing a client and make no network
calls. The package consumer
references only the generated `.nupkg` through the local feed and has no project reference.