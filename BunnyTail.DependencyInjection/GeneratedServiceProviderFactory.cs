namespace BunnyTail.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;

// MEDI 差し替え用ファクトリ (UseServiceProviderFactory で使用)
// Factory for replacing MEDI (used with UseServiceProviderFactory).
public sealed class GeneratedServiceProviderFactory : IServiceProviderFactory<IServiceCollection>
{
    public IServiceCollection CreateBuilder(IServiceCollection services) => services;

    public IServiceProvider CreateServiceProvider(IServiceCollection containerBuilder)
    {
        ArgumentNullException.ThrowIfNull(containerBuilder);
        return new GeneratedServiceProvider(containerBuilder);
    }
}

public static class GeneratedServiceCollectionExtensions
{
    public static GeneratedServiceProvider BuildGeneratedServiceProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new GeneratedServiceProvider(services);
    }
}
