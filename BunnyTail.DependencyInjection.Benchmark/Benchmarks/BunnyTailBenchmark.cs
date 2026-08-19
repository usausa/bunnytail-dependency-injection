namespace BunnyTail.DependencyInjection.Benchmark.Benchmarks;

using Microsoft.Extensions.DependencyInjection;

// BunnyTail.DependencyInjection (Add* 収集による生成経路 / generated path through Add* collection)
public class BunnyTailBenchmark : ProviderBenchmarkBase
{
    protected override IServiceProvider CreateProvider() =>
        new ServiceCollection().AddBenchmarkComponents().BuildGeneratedServiceProvider();
}
