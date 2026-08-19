namespace BunnyTail.DependencyInjection.Tests;

using BunnyTail.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;

public static partial class ConventionRegistration
{
    [ComponentRegistration(Lifetime.Singleton, "Service$")]
    public static partial IServiceCollection AddConventionServices(this IServiceCollection services);

    // Multiple registration methods can live in the same class

    [ComponentRegistration(Lifetime.Scoped, "Repository$")]
    public static partial IServiceCollection AddConventionRepositories(this IServiceCollection services);

    // Generated with the declared accessibility

    [ComponentRegistration(Lifetime.Transient, "Gadget$")]
    private static partial IServiceCollection AddConventionGadgets(this IServiceCollection services);

    public static IServiceCollection AddConventionGadgetsThroughWrapper(this IServiceCollection services) =>
        services.AddConventionGadgets();
}
