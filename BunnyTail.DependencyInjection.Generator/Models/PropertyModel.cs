namespace BunnyTail.DependencyInjection.Generator.Models;

internal sealed record PropertyModel(
    string Name,
    string ServiceType,
    string ServiceTypeName,
    bool InCompilation,
    bool IsValueType,
    int Kind,
    string? KeyLiteral);
