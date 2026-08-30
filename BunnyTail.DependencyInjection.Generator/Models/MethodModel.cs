namespace BunnyTail.DependencyInjection.Generator.Models;

using Microsoft.CodeAnalysis;

using SourceGenerateHelper;

internal sealed record MethodModel(
    string? Namespace,
    string ClassName,
    Accessibility MethodAccessibility,
    string MethodName,
    EquatableArray<PatternModel> Patterns,
    LocationInfo? Location);
