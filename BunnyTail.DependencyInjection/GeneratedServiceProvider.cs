namespace BunnyTail.DependencyInjection;

using System.Runtime.CompilerServices;

using Microsoft.Extensions.DependencyInjection;

// ルートプロバイダ。状態 (Singleton 等) はインスタンスメンバとして保持する (プロセス static は使わない)
// Root provider. State such as singletons is held as instance members (no process-wide statics).
// 基底インタフェースの明示列挙は冗長だが、公開契約を型宣言だけで読めるようにするため残す
// (実装するインタフェース集合は同じで、ディスパッチコストも変わらない)
// Explicitly listing base interfaces is redundant but kept so the public contract is readable from the declaration
// alone (the implemented interface set and dispatch cost are identical either way).
// ReSharper disable RedundantExtendsListEntry
public sealed class GeneratedServiceProvider :
    IServiceProvider,
    IKeyedServiceProvider,
    ISupportRequiredService,
    IServiceScopeFactory,
    IServiceProviderIsService,
    IServiceProviderIsKeyedService,
    IDisposable,
    IAsyncDisposable
// ReSharper restore RedundantExtendsListEntry
{
    internal ServiceProviderScope RootScope { get; }

    internal ServiceRegistry Registry { get; }

    public GeneratedServiceProvider(IEnumerable<ServiceDescriptor> descriptors)
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

    // 型付き解決 (インスタンスメソッドは MEDI 拡張メソッドより優先して束縛される)。
    // ISupportRequiredService の型テストとインタフェース二重ディスパッチを回避する
    // Typed resolution (instance methods bind ahead of the MEDI extension methods),
    // avoiding the ISupportRequiredService type test and the double interface dispatch.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? GetService<T>() => RootScope.GetService<T>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetRequiredService<T>() => RootScope.GetRequiredService<T>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? GetKeyedService<T>(object? serviceKey) => RootScope.GetKeyedService<T>(serviceKey);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetRequiredKeyedService<T>(object? serviceKey) => RootScope.GetRequiredKeyedService<T>(serviceKey);

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
