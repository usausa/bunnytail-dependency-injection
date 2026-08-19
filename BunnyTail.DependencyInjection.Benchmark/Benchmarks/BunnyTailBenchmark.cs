namespace BunnyTail.DependencyInjection.Benchmark.Benchmarks;

using Microsoft.Extensions.DependencyInjection;

public class BunnyTailBenchmark : ProviderBenchmarkBase
{
    protected override IServiceProvider CreateProvider() =>
        new ServiceCollection().AddBenchmarkComponents().BuildGeneratedServiceProvider();
}
