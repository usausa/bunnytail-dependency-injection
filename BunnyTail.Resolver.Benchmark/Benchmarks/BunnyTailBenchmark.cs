namespace BunnyTail.Resolver.Benchmark.Benchmarks;

using Microsoft.Extensions.DependencyInjection;

// BunnyTail.Resolver (Add* 収集による生成経路 / generated path through Add* collection)
public class BunnyTailBenchmark : ResolverBenchmarkBase
{
    protected override IServiceProvider CreateProvider() =>
        new ServiceCollection().AddBenchmarkComponents().BuildResolverServiceProvider();
}
