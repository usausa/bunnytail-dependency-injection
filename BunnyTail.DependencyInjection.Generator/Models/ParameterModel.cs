namespace BunnyTail.DependencyInjection.Generator.Models;

internal sealed record ParameterModel(
    string ServiceType,
    string ServiceTypeName,
    bool InCompilation,
    bool IsValueType,
    bool HasDefaultValue,
    int Kind,
    string? KeyLiteral);
