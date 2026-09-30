namespace BunnyTail.DependencyInjection.Generator.Models;

using SourceGenerateHelper;

internal sealed record PatternModel(
    string Lifetime,
    string? InvalidLifetime,
    string Pattern,
    string? Namespace,
    string? Assembly,
    string? AsType,
    string? AsTypeName,
    bool WithInterfaces,
    LocationInfo? Location);
