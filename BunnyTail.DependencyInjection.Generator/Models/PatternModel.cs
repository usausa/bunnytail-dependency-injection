namespace BunnyTail.DependencyInjection.Generator.Models;

using SourceGenerateHelper;

internal sealed record PatternModel(
    string Lifetime,
    string Pattern,
    string? Namespace,
    string? Assembly,
    string? AsType,
    bool WithInterfaces,
    LocationInfo? Location);
