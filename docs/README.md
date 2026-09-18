# Documentation

The library emits XML documentation during `dotnet build`. The public API uses explicit JSON
converters for the source-compatible wire contract and includes a `JsonSerializerContext` metadata
definition. The transport currently uses its explicit converter options so omitted and explicit-null
request fields remain distinguishable; Native AOT support is not claimed until a trimmed consumer is
validated.

Build documentation without credentials:

```powershell
dotnet build src/TypeSafe.Ai/TypeSafe.Ai.csproj --configuration Release
```