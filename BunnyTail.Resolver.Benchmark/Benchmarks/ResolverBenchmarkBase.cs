namespace BunnyTail.Resolver.Benchmark.Benchmarks;

using BenchmarkDotNet.Attributes;

using BunnyTail.Resolver.Benchmark.Classes;

using Microsoft.Extensions.DependencyInjection;

// 各プロバイダ共通の測定シナリオ。派生クラスがプロバイダの構築だけを差し替える
// Measurement scenarios shared by all providers. Derived classes only swap provider construction.
[Config(typeof(BenchmarkConfig))]
public abstract class ResolverBenchmarkBase
{
    private IServiceProvider provider = default!;

    protected abstract IServiceProvider CreateProvider();

    [GlobalSetup]
    public void Setup()
    {
        provider = CreateProvider();
        Validator.Validate(provider);
    }

    [Benchmark(OperationsPerInvoke = 5)]
    public void Singleton()
    {
        _ = provider.GetService(typeof(ISingleton1));
        _ = provider.GetService(typeof(ISingleton2));
        _ = provider.GetService(typeof(ISingleton3));
        _ = provider.GetService(typeof(ISingleton4));
        _ = provider.GetService(typeof(ISingleton5));
    }

    [Benchmark(OperationsPerInvoke = 5)]
    public void Transient()
    {
        _ = provider.GetService(typeof(ITransient1));
        _ = provider.GetService(typeof(ITransient2));
        _ = provider.GetService(typeof(ITransient3));
        _ = provider.GetService(typeof(ITransient4));
        _ = provider.GetService(typeof(ITransient5));
    }

    [Benchmark(OperationsPerInvoke = 5)]
    public void Combined()
    {
        _ = provider.GetService(typeof(Combined1));
        _ = provider.GetService(typeof(Combined2));
        _ = provider.GetService(typeof(Combined3));
        _ = provider.GetService(typeof(Combined4));
        _ = provider.GetService(typeof(Combined5));
    }

    [Benchmark(OperationsPerInvoke = 5)]
    public void Complex()
    {
        _ = provider.GetService(typeof(Classes.Complex));
        _ = provider.GetService(typeof(Classes.Complex));
        _ = provider.GetService(typeof(Classes.Complex));
        _ = provider.GetService(typeof(Classes.Complex));
        _ = provider.GetService(typeof(Classes.Complex));
    }

    [Benchmark(OperationsPerInvoke = 5)]
    public void Generics()
    {
        _ = provider.GetService(typeof(IGenericObject<string>));
        _ = provider.GetService(typeof(IGenericObject<int>));
        _ = provider.GetService(typeof(IGenericObject<string>));
        _ = provider.GetService(typeof(IGenericObject<int>));
        _ = provider.GetService(typeof(IGenericObject<string>));
    }

    [Benchmark(OperationsPerInvoke = 5)]
    public void MultipleSingleton()
    {
        _ = provider.GetService(typeof(IEnumerable<IMultipleSingletonService>));
        _ = provider.GetService(typeof(IEnumerable<IMultipleSingletonService>));
        _ = provider.GetService(typeof(IEnumerable<IMultipleSingletonService>));
        _ = provider.GetService(typeof(IEnumerable<IMultipleSingletonService>));
        _ = provider.GetService(typeof(IEnumerable<IMultipleSingletonService>));
    }

    [Benchmark(OperationsPerInvoke = 5)]
    public void MultipleTransient()
    {
        _ = provider.GetService(typeof(IEnumerable<IMultipleTransientService>));
        _ = provider.GetService(typeof(IEnumerable<IMultipleTransientService>));
        _ = provider.GetService(typeof(IEnumerable<IMultipleTransientService>));
        _ = provider.GetService(typeof(IEnumerable<IMultipleTransientService>));
        _ = provider.GetService(typeof(IEnumerable<IMultipleTransientService>));
    }

    [Benchmark(OperationsPerInvoke = 5)]
    public void AspNet()
    {
        var factory = (IServiceScopeFactory)provider.GetService(typeof(IServiceScopeFactory))!;
        using (var scope = factory.CreateScope())
        {
            _ = scope.ServiceProvider.GetService(typeof(Controller));
        }

        factory = (IServiceScopeFactory)provider.GetService(typeof(IServiceScopeFactory))!;
        using (var scope = factory.CreateScope())
        {
            _ = scope.ServiceProvider.GetService(typeof(Controller));
        }

        factory = (IServiceScopeFactory)provider.GetService(typeof(IServiceScopeFactory))!;
        using (var scope = factory.CreateScope())
        {
            _ = scope.ServiceProvider.GetService(typeof(Controller));
        }

        factory = (IServiceScopeFactory)provider.GetService(typeof(IServiceScopeFactory))!;
        using (var scope = factory.CreateScope())
        {
            _ = scope.ServiceProvider.GetService(typeof(Controller));
        }

        factory = (IServiceScopeFactory)provider.GetService(typeof(IServiceScopeFactory))!;
        using (var scope = factory.CreateScope())
        {
            _ = scope.ServiceProvider.GetService(typeof(Controller));
        }
    }
}
