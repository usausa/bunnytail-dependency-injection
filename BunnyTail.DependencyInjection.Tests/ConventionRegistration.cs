namespace BunnyTail.DependencyInjection.Tests;

using BunnyTail.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;

public static partial class ConventionRegistration
{
    [ComponentRegistration(Lifetime.Singleton, "Service$")]
    public static partial IServiceCollection AddConventionServices(this IServiceCollection services);

    // 同一クラスに複数の登録メソッドを置ける (生成はクラス単位で 1 ファイル)
    // Multiple registration methods can live in the same class (generation is one file per class).
    [ComponentRegistration(Lifetime.Scoped, "Repository$")]
    public static partial IServiceCollection AddConventionRepositories(this IServiceCollection services);

    // 宣言どおりのアクセシビリティで生成される (public 以外も可)
    // Generated with the declared accessibility (not only public).
    [ComponentRegistration(Lifetime.Transient, "Gadget$")]
    private static partial IServiceCollection AddConventionGadgets(this IServiceCollection services);

    public static IServiceCollection AddConventionGadgetsThroughWrapper(this IServiceCollection services) =>
        services.AddConventionGadgets();
}
