using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace TypeSafe.Ai.PackageTests;

#pragma warning disable CA1707
public sealed class PackageConsumerTests
{
    [Fact]
    public async Task PK01_PK03_and_PK04_packed_consumer_uses_both_endpoints_and_public_exceptions()
    {
        var handler = new ConsumerHandler();
        using var httpClient = new HttpClient(handler);
        using var client = new TypeSafeClient(httpClient, new TypeSafeClientOptions
        {
            ApiKey = "package-secret",
            BaseUri = new Uri("https://package.test"),
            Retry = new RetryPolicy { MaxRetries = 0 },
        });

        IReadOnlyList<ModelCard> models = await client.Models.ListAsync();
        SystemOneResult result = await client.SystemOneAsync(new SystemOneRequest
        {
            State = "state",
            Questions = new Dictionary<string, Question>
            {
                ["q"] = Questions.Noul("question"),
            },
        });

        Assert.Single(models);
        Assert.Equal("jev-latest", result.Model);
        ProductInfoHeaderValue userAgent = handler.Requests[0].Headers.UserAgent.Single();
        Assert.Equal("typesafe-sdk", userAgent.Product!.Name);
        Assert.Equal(TypeSafeVersion.SdkVersion, userAgent.Product.Version);
        Assert.Equal(2, handler.Requests.Count);

        var failingHandler = new ConsumerHandler(returnNotFound: true);
        using var failingHttpClient = new HttpClient(failingHandler);
        using var failingClient = new TypeSafeClient(failingHttpClient, new TypeSafeClientOptions
        {
            ApiKey = "package-secret",
            Retry = new RetryPolicy { MaxRetries = 0 },
        });
        await Assert.ThrowsAsync<NotFoundException>(async () => await failingClient.Models.ListAsync());
    }

    [Fact]
    public void PK02_and_PK05_package_contents_are_allow_listed()
    {
        string packagePath = LocatePackage();
        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        string[] entries = archive.Entries.Select(entry => entry.FullName).OrderBy(name => name).ToArray();
        string[] allowed =
        {
            "TypeSafe.Ai.nuspec",
            "README.md",
            "LICENSE",
            "lib/net10.0/TypeSafe.Ai.dll",
            "lib/net10.0/TypeSafe.Ai.xml",
        };

        Assert.All(entries, entry => Assert.True(
            allowed.Contains(entry, StringComparer.Ordinal) ||
            entry is "_rels/.rels" or "[Content_Types].xml" ||
            entry.StartsWith("package/services/metadata/", StringComparison.Ordinal),
            $"Unexpected package entry: {entry}"));
        Assert.DoesNotContain(entries, entry => entry.Contains("typesafe-sdk-js", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, entry => entry.Contains("work", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, entry => entry.Contains("tests", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("0.6.0", Path.GetFileName(packagePath), StringComparison.Ordinal);
    }

    private static string LocatePackage()
    {
        string? configured = Environment.GetEnvironmentVariable("TYPESAFE_PACKAGE_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
             !File.Exists(Path.Combine(directory.FullName, "TypeSafe.Ai.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        string[] packages = Directory.GetFiles(
            Path.Combine(directory!.FullName, "artifacts", "package"),
            "TypeSafe.Ai.*.nupkg",
            SearchOption.TopDirectoryOnly);
        return Assert.Single(packages);
    }

    private sealed class ConsumerHandler : HttpMessageHandler
    {
        private readonly bool _returnNotFound;
        internal List<(HttpMethod Method, HttpRequestHeaders Headers, string Body)> Requests { get; } = new();

        internal ConsumerHandler(bool returnNotFound = false)
        {
            _returnNotFound = returnNotFound;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.Headers, body));
            if (_returnNotFound)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{\"message\":\"missing\"}", Encoding.UTF8, "application/json"),
                };
            }

            return request.Method == HttpMethod.Get
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"models\":[{\"name\":\"m\",\"description\":\"d\",\"release_date\":\"2026\"}]}",
                        Encoding.UTF8,
                        "application/json"),
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"model\":\"jev-latest\",\"answers\":{\"q\":{\"type\":\"noul\",\"noul\":0.5}},\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}",
                        Encoding.UTF8,
                        "application/json"),
                };
        }
    }
}
#pragma warning restore CA1707