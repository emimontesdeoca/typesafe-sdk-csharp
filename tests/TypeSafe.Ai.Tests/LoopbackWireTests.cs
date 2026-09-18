using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TypeSafe.Ai.Tests;

#pragma warning disable CA1707
public sealed class LoopbackWireTests
{
    [Fact]
    public async Task NT01_real_loopback_wire_headers_protect_sdk_values()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task<string> requestTask = CaptureRequestAsync(listener);

        using var client = new TypeSafeClient(new TypeSafeClientOptions
        {
            ApiKey = "wire-secret",
            BaseUri = new Uri($"http://127.0.0.1:{port}/"),
            DefaultHeaders = new Dictionary<string, string>
            {
                ["AUTHORIZATION"] = "wrong",
                ["content-type"] = "text/plain",
                ["X-TypeSafe-SDK"] = "wrong",
                ["X-Team"] = "wire-test",
            },
            Retry = new RetryPolicy { MaxRetries = 0 },
        });

        await client.SystemOneAsync(new SystemOneRequest
        {
            State = "wire state",
            Questions = new Dictionary<string, Question>
            {
                ["q"] = Questions.Noul("wire question"),
            },
        });

        string request = await requestTask.WaitAsync(TimeSpan.FromSeconds(5));
        listener.Stop();

        Assert.StartsWith("POST /v1/systemone HTTP/1.1", request, StringComparison.Ordinal);
        Assert.Contains("Authorization: Bearer wire-secret", request, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Content-Type: application/json", request, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"X-TypeSafe-SDK: typesafe-sdk/{TypeSafeVersion.SdkVersion}", request, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("X-Team: wire-test", request, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("text/plain", request, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"state\":\"wire state\"", request, StringComparison.Ordinal);
    }

    private static async Task<string> CaptureRequestAsync(TcpListener listener)
    {
        using TcpClient tcpClient = await listener.AcceptTcpClientAsync();
        await using NetworkStream stream = tcpClient.GetStream();
        using var requestBytes = new MemoryStream();
        byte[] buffer = new byte[4096];
        int headerEnd = -1;
        while (headerEnd < 0)
        {
            int read = await stream.ReadAsync(buffer.AsMemory());
            if (read == 0)
            {
                throw new IOException("The loopback client closed before sending headers.");
            }

            requestBytes.Write(buffer, 0, read);
            headerEnd = FindHeaderEnd(requestBytes.GetBuffer(), (int)requestBytes.Length);
        }

        string headers = Encoding.ASCII.GetString(requestBytes.GetBuffer(), 0, headerEnd);
        int contentLength = ParseContentLength(headers);
        while (requestBytes.Length < headerEnd + contentLength)
        {
            int read = await stream.ReadAsync(buffer.AsMemory());
            if (read == 0)
            {
                break;
            }

            requestBytes.Write(buffer, 0, read);
        }

        string request = Encoding.UTF8.GetString(requestBytes.ToArray());
        const string responseBody = "{\"model\":\"jev-latest\",\"answers\":{\"q\":{\"type\":\"noul\",\"noul\":0.5}},\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}";
        byte[] response = Encoding.UTF8.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(responseBody)}\r\nConnection: close\r\n\r\n{responseBody}");
        await stream.WriteAsync(response);
        return request;
    }

    private static int FindHeaderEnd(byte[] bytes, int length)
    {
        for (int index = 3; index < length; index++)
        {
            if (bytes[index - 3] == '\r' && bytes[index - 2] == '\n' &&
                bytes[index - 1] == '\r' && bytes[index] == '\n')
            {
                return index + 1;
            }
        }

        return -1;
    }

    private static int ParseContentLength(string headers)
    {
        foreach (string line in headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                return int.Parse(line[15..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return 0;
    }
}
#pragma warning restore CA1707
