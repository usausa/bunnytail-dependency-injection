namespace BunnyTail.DependencyInjection.Generator.Models;

internal sealed record ParameterModel(
    string TypeName,
    bool InCompilation,
    bool IsValueType,
    int Kind,
    string? KeyLiteral);
