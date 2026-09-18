using System.Text.Json;
using System.Text.Json.Nodes;

namespace TypeSafe.Ai;

/// <summary>Access to the models resource.</summary>
public sealed class ModelsResource
{
    private readonly TypeSafeClient _client;

    internal ModelsResource(TypeSafeClient client)
    {
        _client = client;
    }

    /// <summary>Lists models available to the account.</summary>
    public ApiPromise<IReadOnlyList<ModelCard>> ListAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return _client.RequestAsync(
            HttpMethod.Get,
            "/v1/models",
            hasBody: false,
            body: null,
            options,
            ParseModelsAsync,
            cancellationToken);
    }

    private static Task<IReadOnlyList<ModelCard>> ParseModelsAsync(BufferedResponse response)
    {
        JsonNode? root = response.ParseJson();
        if (root is JsonObject objectRoot && objectRoot["models"] is JsonArray models)
        {
            try
            {
                List<ModelCard> cards = JsonSerializer.Deserialize<List<ModelCard>>(
                    models.ToJsonString(), Json.TypeSafeJson.Options) ?? new List<ModelCard>();
                return Task.FromResult<IReadOnlyList<ModelCard>>(cards);
            }
            catch (JsonException)
            {
            }
        }

        throw new TypeSafeException(
            "Unexpected response shape from GET /v1/models; expected { models: [...] }.");
    }
}
