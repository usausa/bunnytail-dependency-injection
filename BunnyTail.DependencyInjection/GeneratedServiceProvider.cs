namespace BunnyTail.DependencyInjection;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using BunnyTail.DependencyInjection.Internal;

using Microsoft.Extensions.DependencyInjection;

// ReSharper disable RedundantExtendsListEntry
public sealed class GeneratedServiceProvider :
    IServiceProvider,
    IKeyedServiceProvider,
    ISupportRequiredService,
    IServiceScopeFactory,
    IServiceProviderIsService,
    IServiceProviderIsKeyedService,
    ITypeActivator,
    IDisposable,
    IAsyncDisposable
{
    internal ServiceRegistry Registry { get; }

    internal ServiceProviderScope RootScope { get; }

    public GeneratedServiceProvider(IEnumerable<ServiceDescriptor> descriptors)
        : this(descriptors, null)
    {
    }

    public GeneratedServiceProvider(IEnumerable<ServiceDescriptor> descriptors, GeneratedServiceProviderOptions? options)
    {
        Registry = new ServiceRegistry(descriptors, this, options);
        RootScope = new ServiceProviderScope(this, isRootScope: true);
    }

    //--------------------------------------------------------------------------------
    // Dispose
    //--------------------------------------------------------------------------------

    public void Dispose() => RootScope.Dispose();

    public ValueTask DisposeAsync() => RootScope.DisposeAsync();

    //--------------------------------------------------------------------------------
    // IServiceProvider / IKeyedServiceProvider
    //--------------------------------------------------------------------------------

    public object? GetService(Type serviceType) => RootScope.GetService(serviceType);

    public object GetRequiredService(Type serviceType) => RootScope.GetRequiredService(serviceType);

    public object? GetKeyedService(Type serviceType, object? serviceKey) => RootScope.GetKeyedService(serviceType, serviceKey);

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey) => RootScope.GetRequiredKeyedService(serviceType, serviceKey);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? GetService<T>() => RootScope.GetService<T>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetRequiredService<T>() => RootScope.GetRequiredService<T>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? GetKeyedService<T>(object? serviceKey) => RootScope.GetKeyedService<T>(serviceKey);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetRequiredKeyedService<T>(object? serviceKey) => RootScope.GetRequiredKeyedService<T>(serviceKey);

    //--------------------------------------------------------------------------------
    // ITypeActivator
    //--------------------------------------------------------------------------------

    public object Activate(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type)
        => RootScope.Activate(type);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Activate<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class
        => RootScope.Activate<T>();

    //--------------------------------------------------------------------------------
    // IServiceScopeFactory
    //--------------------------------------------------------------------------------

    public IServiceScope CreateScope()
    {
        RootScope.CheckDisposed();
        return new ServiceProviderScope(this, isRootScope: false);
    }

    //--------------------------------------------------------------------------------
    // IServiceProviderIsService / IServiceProviderIsKeyedService
    //--------------------------------------------------------------------------------

    public bool IsService(Type serviceType) => Registry.IsService(new ServiceIdentifier(serviceType, null));

    public bool IsKeyedService(Type serviceType, object? serviceKey) =>
        serviceKey is null ? IsService(serviceType) : Registry.IsService(new ServiceIdentifier(serviceType, serviceKey));
}
// ReSharper restore RedundantExtendsListEntry
