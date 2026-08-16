namespace BunnyTail.Resolver;

using Microsoft.Extensions.DependencyInjection;

// ルートプロバイダ。状態 (Singleton 等) はインスタンスメンバとして保持する (プロセス static は使わない)
// Root provider. State such as singletons is held as instance members (no process-wide statics).
public sealed class ResolverServiceProvider :
    IServiceProvider,
    IKeyedServiceProvider,
    ISupportRequiredService,
    IServiceScopeFactory,
    IServiceProviderIsService,
    IServiceProviderIsKeyedService,
    IDisposable,
    IAsyncDisposable
{
    private readonly ServiceRegistry registry;

    internal ServiceProviderScope RootScope { get; }

    public ResolverServiceProvider(IEnumerable<ServiceDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        RootScope = new ServiceProviderScope(this, isRootScope: true);
        registry = new ServiceRegistry(descriptors, this);
    }

    internal object? ResolveService(ServiceIdentifier id, ServiceProviderScope scope) => registry.Resolve(id, scope);

    //--------------------------------------------------------------------------------
    // IServiceProvider / IKeyedServiceProvider (root スコープへ委譲 / delegated to the root scope)
    //--------------------------------------------------------------------------------

    public object? GetService(Type serviceType) => RootScope.GetService(serviceType);

    public object GetRequiredService(Type serviceType) => RootScope.GetRequiredService(serviceType);

    public object? GetKeyedService(Type serviceType, object? serviceKey) => RootScope.GetKeyedService(serviceType, serviceKey);

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey) => RootScope.GetRequiredKeyedService(serviceType, serviceKey);

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

    public bool IsService(Type serviceType) => registry.IsService(new ServiceIdentifier(serviceType, null));

    public bool IsKeyedService(Type serviceType, object? serviceKey) =>
        serviceKey is null ? IsService(serviceType) : registry.IsService(new ServiceIdentifier(serviceType, serviceKey));

    //--------------------------------------------------------------------------------
    // Dispose
    //--------------------------------------------------------------------------------

    public void Dispose() => RootScope.Dispose();

    public ValueTask DisposeAsync() => RootScope.DisposeAsync();
}
