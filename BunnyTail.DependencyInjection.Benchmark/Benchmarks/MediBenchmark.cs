namespace BunnyTail.DependencyInjection.Benchmark.Benchmarks;

using Microsoft.Extensions.DependencyInjection;

// Microsoft.Extensions.DependencyInjection (基準 / baseline)
public class MediBenchmark : ProviderBenchmarkBase
{
    protected override IServiceProvider CreateProvider() =>
        new ServiceCollection().AddBenchmarkComponents().BuildServiceProvider();
}
