namespace BunnyTail.DependencyInjection;

using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;

public static class TrackingServiceCollectionExtensions
{
    //--------------------------------------------------------------------------------
    // Transient
    //--------------------------------------------------------------------------------

    public static IServiceCollection AddTransient(
        this IServiceCollection services,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type serviceType,
        DisposableTracking tracking)
    {
        services.Add(new TrackingServiceDescriptor(serviceType, serviceType, tracking));
        return services;
    }

    public static IServiceCollection AddTransient(
        this IServiceCollection services,
        Type serviceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType,
        DisposableTracking tracking)
    {
        services.Add(new TrackingServiceDescriptor(serviceType, implementationType, tracking));
        return services;
    }

    public static IServiceCollection AddTransient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TService>(
        this IServiceCollection services,
        DisposableTracking tracking)
        where TService : class
    {
        services.Add(new TrackingServiceDescriptor(typeof(TService), typeof(TService), tracking));
        return services;
    }

    public static IServiceCollection AddTransient<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
        this IServiceCollection services,
        DisposableTracking tracking)
        where TService : class
        where TImplementation : class, TService
    {
        services.Add(new TrackingServiceDescriptor(typeof(TService), typeof(TImplementation), tracking));
        return services;
    }

    public static IServiceCollection AddTransient<TService>(
        this IServiceCollection services,
        Func<IServiceProvider, TService> implementationFactory,
        DisposableTracking tracking)
        where TService : class
    {
        services.Add(new TrackingServiceDescriptor(typeof(TService), implementationFactory, tracking));
        return services;
    }

    //--------------------------------------------------------------------------------
    // Keyed transient
    //--------------------------------------------------------------------------------

    public static IServiceCollection AddKeyedTransient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TService>(
        this IServiceCollection services,
        object? serviceKey,
        DisposableTracking tracking)
        where TService : class
    {
        services.Add(new TrackingServiceDescriptor(typeof(TService), serviceKey, typeof(TService), tracking));
        return services;
    }

    public static IServiceCollection AddKeyedTransient<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
        this IServiceCollection services,
        object? serviceKey,
        DisposableTracking tracking)
        where TService : class
        where TImplementation : class, TService
    {
        services.Add(new TrackingServiceDescriptor(typeof(TService), serviceKey, typeof(TImplementation), tracking));
        return services;
    }

    public static IServiceCollection AddKeyedTransient<TService>(
        this IServiceCollection services,
        object? serviceKey,
        Func<IServiceProvider, object?, TService> implementationFactory,
        DisposableTracking tracking)
        where TService : class
    {
        services.Add(new TrackingServiceDescriptor(typeof(TService), serviceKey, implementationFactory, tracking));
        return services;
    }
}
