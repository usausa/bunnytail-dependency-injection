namespace BunnyTail.DependencyInjection.Generator.Models;

using SourceGenerateHelper;

internal sealed record MethodModel(
    string? Namespace,
    string ClassName,
    string Signature,
    string ParameterName,
    EquatableArray<PatternModel> Patterns,
    LocationInfo? Location);
