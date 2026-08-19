namespace BunnyTail.DependencyInjection.Benchmark.Benchmarks;

using Microsoft.Extensions.DependencyInjection;

public class MediBenchmark : ProviderBenchmarkBase
{
    protected override IServiceProvider CreateProvider() =>
        new ServiceCollection().AddBenchmarkComponents().BuildServiceProvider();
}
