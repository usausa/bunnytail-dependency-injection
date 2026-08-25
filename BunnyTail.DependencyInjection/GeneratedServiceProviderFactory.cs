namespace BunnyTail.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;

public sealed class GeneratedServiceProviderFactory : IServiceProviderFactory<IServiceCollection>
{
    private readonly GeneratedServiceProviderOptions? options;

    public GeneratedServiceProviderFactory()
    {
    }

    public GeneratedServiceProviderFactory(Action<GeneratedServiceProviderOptions> configure)
    {
        options = new GeneratedServiceProviderOptions();
        configure(options);
    }

    public IServiceCollection CreateBuilder(IServiceCollection services) => services;

    public IServiceProvider CreateServiceProvider(IServiceCollection containerBuilder)
    {
        return new GeneratedServiceProvider(containerBuilder, options);
    }
}

public static class GeneratedServiceCollectionExtensions
{
    public static GeneratedServiceProvider BuildGeneratedServiceProvider(this IServiceCollection services)
    {
        return new GeneratedServiceProvider(services);
    }

    public static GeneratedServiceProvider BuildGeneratedServiceProvider(this IServiceCollection services, Action<GeneratedServiceProviderOptions> configure)
    {
        var options = new GeneratedServiceProviderOptions();
        configure(options);
        return new GeneratedServiceProvider(services, options);
    }
}
