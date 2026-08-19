namespace BunnyTail.DependencyInjection.Benchmark.Benchmarks;

using BenchmarkDotNet.Attributes;

using BunnyTail.DependencyInjection.Benchmark.Classes;

using Microsoft.Extensions.DependencyInjection;

[Config(typeof(BenchmarkConfig))]
public abstract class ProviderBenchmarkBase
{
    private IServiceProvider provider = default!;

    private IServiceScope serviceScope = default!;

    private IServiceProvider scopeProvider = default!;

    protected abstract IServiceProvider CreateProvider();

    [GlobalSetup]
    public void Setup()
    {
        provider = CreateProvider();
        Validator.Validate(provider);

        serviceScope = provider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        scopeProvider = serviceScope.ServiceProvider;
        _ = scopeProvider.GetService(typeof(IScoped1));
        _ = scopeProvider.GetService(typeof(IScoped2));
        _ = scopeProvider.GetService(typeof(IScoped3));
        _ = scopeProvider.GetService(typeof(IScoped4));
        _ = scopeProvider.GetService(typeof(IScoped5));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        serviceScope.Dispose();
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
        _ = provider.GetService(typeof(Complex));
        _ = provider.GetService(typeof(Complex));
        _ = provider.GetService(typeof(Complex));
        _ = provider.GetService(typeof(Complex));
        _ = provider.GetService(typeof(Complex));
    }

    [Benchmark(OperationsPerInvoke = 5)]
    public void Generics()
    {
        if (Registrations.SkipGenerics)
        {
            return;
        }

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
