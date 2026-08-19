namespace BunnyTail.DependencyInjection.Generator.Models;

internal sealed record PropertyModel(
    string Name,
    string TypeName,
    bool InCompilation,
    bool IsValueType,
    int Kind,
    string? KeyLiteral);
