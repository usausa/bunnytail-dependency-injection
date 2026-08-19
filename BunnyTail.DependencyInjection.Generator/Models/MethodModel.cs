namespace BunnyTail.DependencyInjection.Generator.Models;

using Microsoft.CodeAnalysis;

using SourceGenerateHelper;

// [ComponentRegistration] 付き partial メソッド
// Partial method annotated with [ComponentRegistration].
internal sealed record MethodModel(
    string? Namespace,
    string ClassName,
    Accessibility MethodAccessibility,
    string MethodName,
    EquatableArray<PatternModel> Patterns,
    LocationInfo? Location);
