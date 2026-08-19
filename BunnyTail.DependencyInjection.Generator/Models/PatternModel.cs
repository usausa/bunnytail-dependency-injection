namespace BunnyTail.DependencyInjection.Generator.Models;

// [ComponentRegistration] のパターン指定
// Pattern specification of [ComponentRegistration].
internal sealed record PatternModel(
    string Lifetime,
    string Pattern,
    string? Namespace,
    string? Assembly);
