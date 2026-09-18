using System.Runtime.InteropServices;

namespace TypeSafe.Ai;

internal static class RuntimeDescription
{
    internal static bool IsBrowser => OperatingSystem.IsBrowser();

    internal static string Current { get; } = Describe();

    private static string Describe()
    {
        string platform = RuntimeInformation.RuntimeIdentifier.Split('-')[0];
        return $"dotnet/{Environment.Version} ({platform}; {RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()})";
    }
}
