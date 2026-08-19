namespace Example;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Add* calls are collected and generate factories for the registered types
internal interface IManualService
{
    string Describe();
}

internal sealed class ManualService : IManualService
{
    public string Describe() => "manual singleton";
}

internal sealed class ManualScopedService
{
    public Guid Id { get; } = Guid.NewGuid();
}

// Open generic registration. Closed forms appearing in code (here int, through the ManualConsumer dependency)
internal interface IManualBox<out T>
{
#pragma warning disable IDE0051
    T? Value { get; }
#pragma warning restore IDE0051
}

internal sealed class ManualBox<T> : IManualBox<T>
{
    public T? Value => default;
}

internal interface IManualPlugin
{
    string Name { get; }
}

internal sealed class ManualPluginA : IManualPlugin
{
    public string Name => "A";
}

internal sealed class ManualPluginB : IManualPlugin
{
    public string Name => "B";
}

internal interface IManualKeyed
{
    string Kind { get; }
}

internal sealed class PrimaryManualKeyed : IManualKeyed
{
    public string Kind => "primary";
}

internal sealed class ManualConsumer
{
    public ManualConsumer(IManualService service, ManualScopedService scoped, IManualBox<int> box)
    {
        Service = service;
        Scoped = scoped;
        Box = box;
    }

    public IManualService Service { get; }

    public ManualScopedService Scoped { get; }

    public IManualBox<int> Box { get; }
}

internal static class ManualRegistrations
{
    // Shapes that produce generated factories: generic, typeof, keyed, ServiceDescriptor and TryAddEnumerable
    public static IServiceCollection AddManualServices(this IServiceCollection services)
    {
        services.AddSingleton<IManualService, ManualService>();
        services.AddScoped<ManualScopedService>();
        services.AddTransient(typeof(IManualBox<>), typeof(ManualBox<>));
        services.AddTransient<ManualConsumer>();
        services.AddKeyedSingleton<IManualKeyed, PrimaryManualKeyed>("primary");
        services.TryAddEnumerable(ServiceDescriptor.Transient<IManualPlugin, ManualPluginA>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IManualPlugin, ManualPluginB>());

        // Factory registrations are not collected because the container does not instantiate the type (resolved on the runtime path)
        services.AddSingleton(static _ => new ManualOptions("from factory"));
        return services;
    }
}

internal sealed class ManualOptions
{
    public ManualOptions(string value)
    {
        Value = value;
    }

    public string Value { get; }
}
