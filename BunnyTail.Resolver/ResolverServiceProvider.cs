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
    internal ServiceProviderScope RootScope { get; }

    internal ServiceRegistry Registry { get; }

    public ResolverServiceProvider(IEnumerable<ServiceDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        // registry を先に構築する。scope は registry 参照を直接保持するため (S-10)。
        // warmup はアクセサの実現のみで、scope 経由の解決は発生しない
        // The registry is built first because scopes hold a direct registry reference (S-10).
        // Warmup only realizes accessors; no resolution goes through a scope.
        Registry = new ServiceRegistry(descriptors, this);
        RootScope = new ServiceProviderScope(this, isRootScope: true);
    }

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

    public bool IsService(Type serviceType) => Registry.IsService(new ServiceIdentifier(serviceType, null));

    public bool IsKeyedService(Type serviceType, object? serviceKey) =>
        serviceKey is null ? IsService(serviceType) : Registry.IsService(new ServiceIdentifier(serviceType, serviceKey));

    //--------------------------------------------------------------------------------
    // Dispose
    //--------------------------------------------------------------------------------

    public void Dispose() => RootScope.Dispose();

    public ValueTask DisposeAsync() => RootScope.DisposeAsync();
}
