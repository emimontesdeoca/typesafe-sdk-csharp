using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TypeSafe.Ai;

internal sealed class BufferedResponse
{
    internal BufferedResponse(
        HttpStatusCode statusCode,
        string? reasonPhrase,
        Version version,
        HttpMethod method,
        Uri requestUri,
        IReadOnlyDictionary<string, string> headers,
        IReadOnlyDictionary<string, string> contentHeaders,
        byte[] body)
    {
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
        Version = version;
        Method = method;
        RequestUri = requestUri;
        Headers = headers;
        ContentHeaders = contentHeaders;
        Body = body;
    }

    internal HttpStatusCode StatusCode { get; }
    internal string? ReasonPhrase { get; }
    internal Version Version { get; }
    internal HttpMethod Method { get; }
    internal Uri RequestUri { get; }
    internal IReadOnlyDictionary<string, string> Headers { get; }
    internal IReadOnlyDictionary<string, string> ContentHeaders { get; }
    internal byte[] Body { get; }
    internal int Status => (int)StatusCode;
    internal bool IsSuccessStatusCode => Status is >= 200 and <= 299;
    internal string? RequestId => ApiException.HeaderValue(Headers, "x-typesafe-request-id");

    internal HttpResponseMessage CreateResponse()
    {
        var response = new HttpResponseMessage(StatusCode)
        {
            ReasonPhrase = ReasonPhrase,
            Version = Version,
            RequestMessage = new HttpRequestMessage(Method, RequestUri),
            Content = new ByteArrayContent(Body),
        };
        CopyHeaders(Headers, response.Headers);
        CopyHeaders(ContentHeaders, response.Content.Headers);
        return response;
    }

    internal ParsedErrorBody ParseBody()
    {
        string text = Encoding.UTF8.GetString(Body);
        return ErrorBodyParser.Parse(text);
    }

    internal JsonNode? ParseJson()
    {
        if (Body.Length == 0)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(Encoding.UTF8.GetString(Body));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void CopyHeaders(
        IReadOnlyDictionary<string, string> source,
        HttpHeaders target)
    {
        foreach ((string name, string value) in source)
        {
            target.TryAddWithoutValidation(name, value);
        }
    }
}
