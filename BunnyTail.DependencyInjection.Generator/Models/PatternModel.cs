namespace BunnyTail.DependencyInjection.Generator.Models;

using SourceGenerateHelper;

// [ComponentRegistration] のパターン指定
// Pattern specification of [ComponentRegistration].
internal sealed record PatternModel(
    string Lifetime,
    string Pattern,
    string? Namespace,
    string? Assembly,
    string? AsType,
    bool WithInterfaces,
    LocationInfo? Location);
