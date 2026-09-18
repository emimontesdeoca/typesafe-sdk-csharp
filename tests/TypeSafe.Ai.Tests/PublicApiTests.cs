using System.Reflection;

namespace TypeSafe.Ai.Tests;

#pragma warning disable CA1707
public sealed class PublicApiTests
{
    [Fact]
    public void TY01_TY04_expected_public_types_and_api_promise_members_are_present()
    {
        Assembly assembly = typeof(TypeSafeClient).Assembly;
        string[] expectedTypes =
        {
            "TypeSafe.Ai.TypeSafeClient",
            "TypeSafe.Ai.ModelsResource",
            "TypeSafe.Ai.ApiPromise`1",
            "TypeSafe.Ai.WithResponse`1",
            "TypeSafe.Ai.Question",
            "TypeSafe.Ai.NoulQuestion",
            "TypeSafe.Ai.ChoiceQuestion",
            "TypeSafe.Ai.ScoreQuestion",
            "TypeSafe.Ai.NoulAnswer",
            "TypeSafe.Ai.ChoiceAnswer",
            "TypeSafe.Ai.ScoreAnswer",
            "TypeSafe.Ai.ApiException",
            "TypeSafe.Ai.ApiConnectionException",
            "TypeSafe.Ai.ApiTimeoutException",
            "TypeSafe.Ai.ApiUserAbortException",
            "TypeSafe.Ai.RetryPolicy",
            "TypeSafe.Ai.TypeSafeLogLevel",
        };

        foreach (string typeName in expectedTypes)
        {
            Assert.NotNull(assembly.GetType(typeName, throwOnError: false));
        }

        Type promise = typeof(ApiPromise<>);
        Assert.NotNull(promise.GetMethod(nameof(ApiPromise<int>.GetAwaiter)));
        Assert.NotNull(promise.GetMethod(nameof(ApiPromise<int>.AsResponseAsync)));
        Assert.NotNull(promise.GetMethod(nameof(ApiPromise<int>.WithResponseAsync)));
        Assert.NotNull(promise.GetMethod(nameof(ApiPromise<int>.Map)));
        Assert.DoesNotContain(typeof(TypeSafeClient).GetProperties(), property =>
            property.Name.Equals("ApiKey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PK02_version_matches_assembly_and_package_version_property()
    {
        Assembly assembly = typeof(TypeSafeClient).Assembly;
        string informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

        Assert.StartsWith(TypeSafeVersion.SdkVersion, informational, StringComparison.Ordinal);
        Assert.Equal("0.6.0", TypeSafeVersion.SdkVersion);
    }
}
#pragma warning restore CA1707
