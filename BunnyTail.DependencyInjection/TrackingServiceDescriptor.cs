namespace BunnyTail.DependencyInjection;

using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;

public sealed class TrackingServiceDescriptor : ServiceDescriptor
{
    public DisposableTracking Tracking { get; }

    public TrackingServiceDescriptor(
        Type serviceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType,
        DisposableTracking tracking)
        : base(serviceType, implementationType, ServiceLifetime.Transient)
    {
        Tracking = tracking;
    }

    public TrackingServiceDescriptor(
        Type serviceType,
        Func<IServiceProvider, object> factory,
        DisposableTracking tracking)
        : base(serviceType, factory, ServiceLifetime.Transient)
    {
        Tracking = tracking;
    }

    public TrackingServiceDescriptor(
        Type serviceType,
        object? serviceKey,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType,
        DisposableTracking tracking)
        : base(serviceType, serviceKey, implementationType, ServiceLifetime.Transient)
    {
        Tracking = tracking;
    }

    public TrackingServiceDescriptor(
        Type serviceType,
        object? serviceKey,
        Func<IServiceProvider, object?, object> factory,
        DisposableTracking tracking)
        : base(serviceType, serviceKey, factory, ServiceLifetime.Transient)
    {
        Tracking = tracking;
    }
}
