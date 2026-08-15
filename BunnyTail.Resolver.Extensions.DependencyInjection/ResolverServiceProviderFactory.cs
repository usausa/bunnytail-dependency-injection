namespace BunnyTail.Resolver;

using Microsoft.Extensions.DependencyInjection;

// MEDI 差し替え用ファクトリ (UseServiceProviderFactory で使用)
public sealed class ResolverServiceProviderFactory : IServiceProviderFactory<IServiceCollection>
{
    public IServiceCollection CreateBuilder(IServiceCollection services) => services;

    public IServiceProvider CreateServiceProvider(IServiceCollection containerBuilder)
    {
        ArgumentNullException.ThrowIfNull(containerBuilder);
        return new ResolverServiceProvider(containerBuilder);
    }
}

public static class ResolverServiceCollectionExtensions
{
    public static ResolverServiceProvider BuildResolverServiceProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new ResolverServiceProvider(services);
    }
}
