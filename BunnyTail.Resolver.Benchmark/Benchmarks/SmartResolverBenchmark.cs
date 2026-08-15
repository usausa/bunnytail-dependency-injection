namespace BunnyTail.Resolver.Benchmark.Benchmarks;

using Microsoft.Extensions.DependencyInjection;

using Smart.Resolver;

// Smart.Resolver (MEDI ブリッジ経由 / through the MEDI bridge)
public class SmartResolverBenchmark : ResolverBenchmarkBase
{
    protected override IServiceProvider CreateProvider()
    {
        var factory = new SmartServiceProviderFactory();
        return factory.CreateServiceProvider(factory.CreateBuilder(new ServiceCollection().AddBenchmarkComponents()));
    }
}
