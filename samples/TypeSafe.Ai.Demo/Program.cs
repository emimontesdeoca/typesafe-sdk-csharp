using TypeSafe.Ai;

if (TypeSafeEnvironment.Read(TypeSafeEnvironment.ApiKey) is null)
{
    Console.WriteLine($"Configura {TypeSafeEnvironment.ApiKey} antes de ejecutar la demo.");
    Console.WriteLine($"PowerShell: $env:{TypeSafeEnvironment.ApiKey} = \"tu-api-key\"");
    return;
}

Console.Write("Texto para analizar: ");
string? state = Console.ReadLine();
if (string.IsNullOrWhiteSpace(state))
{
    Console.WriteLine("Debes escribir un texto.");
    return;
}

using var client = new TypeSafeClient();

try
{
    SystemOneResult result = await client.SystemOneAsync(new SystemOneRequest
    {
        State = state,
        Questions = new Dictionary<string, Question>
        {
            ["isBilling"] = Questions.Noul("¿Es un problema de facturación?"),
        },
    });

    NoulAnswer answer = (NoulAnswer)result.Answers["isBilling"];
    Console.WriteLine($"Probabilidad de facturación: {answer.Noul:P1}");
}
catch (ApiException exception)
{
    Console.Error.WriteLine($"Error de TypeSafe API ({exception.Status}): {exception.Message}");
}
