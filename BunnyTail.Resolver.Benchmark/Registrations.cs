namespace BunnyTail.Resolver.Benchmark;

using BunnyTail.Resolver.Benchmark.Classes;

using Microsoft.Extensions.DependencyInjection;

// 全プロバイダ共通の登録。同一の IServiceCollection を MEDI / BunnyTail / Smart の各ファクトリへ渡して比較する
// Registrations shared by all providers. The same IServiceCollection is handed to the MEDI / BunnyTail / Smart factories for comparison.
internal static class Registrations
{
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
        services.AddTransient(typeof(IGenericObject<>), typeof(GenericObject<>));
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

        // ASP.NET Core simulation
        services.AddScoped<IScopedService, ScopedService>();
        services.AddTransient<ITransientService1, TransientService1>();
        services.AddTransient<ITransientService2, TransientService2>();
        services.AddTransient<ITransientService3, TransientService3>();
        services.AddTransient<Controller>();

        return services;
    }
}
