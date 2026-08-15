namespace BunnyTail.Resolver.Tests;

using BunnyTail.Resolver;

using Microsoft.Extensions.DependencyInjection;

// 命名規約ベース登録のアンカー宣言 (本体はジェネレータが生成)
// Anchor declaration for convention based registration (the body is generated).
public static partial class ConventionRegistration
{
    [ComponentRegistration(Lifetime.Singleton, "Service$")]
    public static partial IServiceCollection AddConventionServices(this IServiceCollection services);
}
