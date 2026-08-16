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

    private IServiceScope scope = default!;

    private IServiceProvider scopeProvider = default!;

    protected abstract IServiceProvider CreateProvider();

    [GlobalSetup]
    public void Setup()
    {
        provider = CreateProvider();
        Validator.Validate(provider);

        // Scoped は解決済みスコープからの読み出しを測るため、スコープ生成と初回解決を事前に済ませる
        // Scoped measures reads from an already-populated scope, so scope creation and first resolution happen up front.
        scope = provider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        scopeProvider = scope.ServiceProvider;
        _ = scopeProvider.GetService(typeof(IScoped1));
        _ = scopeProvider.GetService(typeof(IScoped2));
        _ = scopeProvider.GetService(typeof(IScoped3));
        _ = scopeProvider.GetService(typeof(IScoped4));
        _ = scopeProvider.GetService(typeof(IScoped5));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        scope.Dispose();
        (provider as IDisposable)?.Dispose();
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
    public void Scoped()
    {
        _ = scopeProvider.GetService(typeof(IScoped1));
        _ = scopeProvider.GetService(typeof(IScoped2));
        _ = scopeProvider.GetService(typeof(IScoped3));
        _ = scopeProvider.GetService(typeof(IScoped4));
        _ = scopeProvider.GetService(typeof(IScoped5));
    }

    [Benchmark(OperationsPerInvoke = 5)]
    public void Keyed()
    {
        _ = ((IKeyedServiceProvider)provider).GetRequiredKeyedService(typeof(IKeyedService), "key1");
        _ = ((IKeyedServiceProvider)provider).GetRequiredKeyedService(typeof(IKeyedService), "key2");
        _ = ((IKeyedServiceProvider)provider).GetRequiredKeyedService(typeof(IKeyedService), "key3");
        _ = ((IKeyedServiceProvider)provider).GetRequiredKeyedService(typeof(IKeyedService), "key4");
        _ = ((IKeyedServiceProvider)provider).GetRequiredKeyedService(typeof(IKeyedService), "key5");
    }

    // 列挙まで行う。Smart.Resolver は遅延列挙 (要素は列挙時に解決) のため、取得だけではラッパー確保しか測れない
    // Enumerate the result: Smart.Resolver materializes lazily, so fetching alone would only measure wrapper allocation.
    [Benchmark(OperationsPerInvoke = 5)]
    public void MultipleSingleton()
    {
        EnumerateMultipleSingleton();
        EnumerateMultipleSingleton();
        EnumerateMultipleSingleton();
        EnumerateMultipleSingleton();
        EnumerateMultipleSingleton();
    }

    private void EnumerateMultipleSingleton()
    {
        foreach (var service in (IEnumerable<IMultipleSingletonService>)provider.GetService(typeof(IEnumerable<IMultipleSingletonService>))!)
        {
            _ = service;
        }
    }

    [Benchmark(OperationsPerInvoke = 5)]
    public void MultipleTransient()
    {
        EnumerateMultipleTransient();
        EnumerateMultipleTransient();
        EnumerateMultipleTransient();
        EnumerateMultipleTransient();
        EnumerateMultipleTransient();
    }

    private void EnumerateMultipleTransient()
    {
        foreach (var service in (IEnumerable<IMultipleTransientService>)provider.GetService(typeof(IEnumerable<IMultipleTransientService>))!)
        {
            _ = service;
        }
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
