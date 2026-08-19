namespace BunnyTail.DependencyInjection.Benchmark;

using BunnyTail.DependencyInjection.Benchmark.Classes;

using Microsoft.Extensions.DependencyInjection;

internal static class Registrations
{
    // MEDI cannot resolve open generics with value type arguments on NativeAOT
    internal static readonly bool SkipGenerics = Environment.GetEnvironmentVariable("BUNNYTAIL_BENCH_NO_GENERICS") == "1";

    public static IServiceCollection AddBenchmarkComponents(this IServiceCollection services)
    {
        services.AddSingleton<ISingleton1, Singleton1>();
        services.AddSingleton<ISingleton2, Singleton2>();
        services.AddSingleton<ISingleton3, Singleton3>();
        services.AddSingleton<ISingleton4, Singleton4>();
        services.AddSingleton<ISingleton5, Singleton5>();
        services.AddTransient<ITransient1, Transient1>();
        services.AddTransient<ITransient2, Transient2>();
        services.AddTransient<ITransient3, Transient3>();
        services.AddTransient<ITransient4, Transient4>();
        services.AddTransient<ITransient5, Transient5>();
        services.AddTransient<Combined1>();
        services.AddTransient<Combined2>();
        services.AddTransient<Combined3>();
        services.AddTransient<Combined4>();
        services.AddTransient<Combined5>();
        services.AddTransient<Complex>();
        if (!SkipGenerics)
        {
            services.AddTransient(typeof(IGenericObject<>), typeof(GenericObject<>));
        }

        services.AddSingleton<IMultipleSingletonService, MultipleSingletonService1>();
        services.AddSingleton<IMultipleSingletonService, MultipleSingletonService2>();
        services.AddSingleton<IMultipleSingletonService, MultipleSingletonService3>();
        services.AddSingleton<IMultipleSingletonService, MultipleSingletonService4>();
        services.AddSingleton<IMultipleSingletonService, MultipleSingletonService5>();
        services.AddTransient<IMultipleTransientService, MultipleTransientService1>();
        services.AddTransient<IMultipleTransientService, MultipleTransientService2>();
        services.AddTransient<IMultipleTransientService, MultipleTransientService3>();
        services.AddTransient<IMultipleTransientService, MultipleTransientService4>();
        services.AddTransient<IMultipleTransientService, MultipleTransientService5>();
        services.AddScoped<IScoped1, Scoped1>();
        services.AddScoped<IScoped2, Scoped2>();
        services.AddScoped<IScoped3, Scoped3>();
        services.AddScoped<IScoped4, Scoped4>();
        services.AddScoped<IScoped5, Scoped5>();
        services.AddKeyedSingleton<IKeyedService, KeyedService1>("key1");
        services.AddKeyedSingleton<IKeyedService, KeyedService2>("key2");
        services.AddKeyedSingleton<IKeyedService, KeyedService3>("key3");
        services.AddKeyedSingleton<IKeyedService, KeyedService4>("key4");
        services.AddKeyedSingleton<IKeyedService, KeyedService5>("key5");

        // ASP.NET Core simulation
        services.AddScoped<IScopedService, ScopedService>();
        services.AddTransient<ITransientService1, TransientService1>();
        services.AddTransient<ITransientService2, TransientService2>();
        services.AddTransient<ITransientService3, TransientService3>();
        services.AddTransient<Controller>();

        return services;
    }
}
