namespace BunnyTail.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;

public sealed class GeneratedServiceProviderFactory : IServiceProviderFactory<IServiceCollection>
{
    public IServiceCollection CreateBuilder(IServiceCollection services) => services;

    public IServiceProvider CreateServiceProvider(IServiceCollection containerBuilder)
    {
        return new GeneratedServiceProvider(containerBuilder);
    }
}

public static class GeneratedServiceCollectionExtensions
{
    public static GeneratedServiceProvider BuildGeneratedServiceProvider(this IServiceCollection services)
    {
        return new GeneratedServiceProvider(services);
    }
}
