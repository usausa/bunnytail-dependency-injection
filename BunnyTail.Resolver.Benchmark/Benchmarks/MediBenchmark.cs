namespace BunnyTail.Resolver.Benchmark.Benchmarks;

using Microsoft.Extensions.DependencyInjection;

// Microsoft.Extensions.DependencyInjection (基準 / baseline)
public class MediBenchmark : ResolverBenchmarkBase
{
    protected override IServiceProvider CreateProvider() =>
        new ServiceCollection().AddBenchmarkComponents().BuildServiceProvider();
}
