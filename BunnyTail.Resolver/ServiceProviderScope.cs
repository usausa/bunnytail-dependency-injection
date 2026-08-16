namespace BunnyTail.Resolver;

using System.Runtime.CompilerServices;

using Microsoft.Extensions.DependencyInjection;

// スコープ。ルートプロバイダも root スコープとして同じ実装を使う。注入される IServiceProvider はこのスコープ自身 (MEDI 互換)
// Scope. The root provider uses the same implementation as its root scope. The injected IServiceProvider is this scope itself (MEDI compatible).
public sealed class ServiceProviderScope :
    IServiceScope,
    IServiceProvider,
    IKeyedServiceProvider,
    ISupportRequiredService,
    IDisposable,
    IAsyncDisposable
{
    private static readonly object NullSentinel = new();

    private readonly ResolverServiceProvider provider;

    // 解決に使う registry 参照。dispose 時に「必ず throw する番兵」へ差し替えることで、
    // ホット経路から disposed フラグの分岐を消す (S-10)。MEDI と同じく dispose 競合時は best-effort
    // Registry reference used for resolution. Swapped to an always-throwing sentinel on dispose, which removes
    // the disposed-flag branch from the hot path (S-10). Like MEDI, racing with dispose is best-effort.
    private ServiceRegistry registry;

    private object?[] slots = [];

    private List<object>? disposables;

    private bool disposed;

    internal ServiceProviderScope(ResolverServiceProvider provider, bool isRootScope)
    {
        this.provider = provider;
        registry = provider.Registry;
        IsRootScope = isRootScope;
    }

    internal bool IsRootScope { get; }

    internal ServiceProviderScope RootScope => IsRootScope ? this : provider.RootScope;

    internal object Sync { get; } = new();

    public IServiceProvider ServiceProvider => this;

    //--------------------------------------------------------------------------------
    // Resolve (解決)
    //--------------------------------------------------------------------------------

    public object? GetService(Type serviceType) => registry.Resolve(new ServiceIdentifier(serviceType, null), this);

    public object GetRequiredService(Type serviceType)
    {
        var service = GetService(serviceType);
        if (service is null)
        {
            throw new InvalidOperationException($"No service for type '{serviceType}' has been registered.");
        }

        return service;
    }

    public object? GetKeyedService(Type serviceType, object? serviceKey)
    {
        if (serviceKey is null)
        {
            return GetService(serviceType);
        }

        if (ReferenceEquals(serviceKey, KeyedService.AnyKey) && !IsEnumerableService(serviceType))
        {
            throw new InvalidOperationException("KeyedService.AnyKey can only be used to retrieve an IEnumerable of keyed services.");
        }

        return registry.Resolve(new ServiceIdentifier(serviceType, serviceKey), this);
    }

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
    {
        var service = GetKeyedService(serviceType, serviceKey);
        if (service is null)
        {
            throw serviceKey is null
                ? new InvalidOperationException($"No service for type '{serviceType}' has been registered.")
                : new InvalidOperationException($"No service for type '{serviceType}' and service key '{serviceKey}' has been registered.");
        }

        return service;
    }

    private static bool IsEnumerableService(Type serviceType) =>
        serviceType.IsConstructedGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>);

    // 生成コード用の型付き解決。sealed クラスへの直接呼び出しになり、MEDI 拡張メソッドが行う
    // ISupportRequiredService の型テストとインタフェース二重ディスパッチを回避する
    // Typed resolution for generated code. Direct calls on a sealed class, avoiding the
    // ISupportRequiredService type test and the double interface dispatch of the MEDI extension methods.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetRequiredService<T>() => (T)GetRequiredService(typeof(T));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetRequiredKeyedService<T>(object? serviceKey) => (T)GetRequiredKeyedService(typeof(T), serviceKey);

    //--------------------------------------------------------------------------------
    // Slot storage (スロット保持)
    //--------------------------------------------------------------------------------

    internal static object WrapSlotValue(object? value) => value ?? NullSentinel;

    internal static object? UnwrapSlotValue(object value) => ReferenceEquals(value, NullSentinel) ? null : value;

    internal object? GetSlot(int index)
    {
        var array = slots;
        return (uint)index < (uint)array.Length ? array[index] : null;
    }

    internal void SetSlotUnderLock(int index, object? value)
    {
        var array = slots;
        if (index >= array.Length)
        {
            var newArray = new object?[((index >> 3) << 3) + 8];
            Array.Copy(array, newArray, array.Length);
            slots = newArray;
            array = newArray;
        }

        array[index] = WrapSlotValue(value);
    }

    //--------------------------------------------------------------------------------
    // Disposal tracking (disposal 追跡)
    //--------------------------------------------------------------------------------

    internal void CheckDisposed() => ObjectDisposedException.ThrowIf(disposed, typeof(IServiceProvider));

    internal void CaptureDisposable(object? value)
    {
        if (value is not IDisposable && value is not IAsyncDisposable)
        {
            return;
        }

        lock (Sync)
        {
            CaptureDisposableUnderLock(value);
        }
    }

    internal void CaptureDisposableUnderLock(object? value)
    {
        if (value is not IDisposable && value is not IAsyncDisposable)
        {
            return;
        }

        if (disposed)
        {
            // dispose 済みスコープからの生成物は即時破棄して例外 (MEDI 互換)
            // Instances created from a disposed scope are disposed immediately and an exception is thrown (MEDI compatible).
            if (value is IDisposable d)
            {
                d.Dispose();
            }

            throw new ObjectDisposedException(nameof(IServiceProvider));
        }

        (disposables ??= []).Add(value);
    }

    //--------------------------------------------------------------------------------
    // Dispose (生成の逆順 = LIFO / reverse creation order)
    //--------------------------------------------------------------------------------

    public void Dispose()
    {
        List<object>? toDispose;
        lock (Sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            registry = ServiceRegistry.DisposedSentinel;
            toDispose = disposables;
            disposables = null;
        }

        if (toDispose is null)
        {
            return;
        }

        for (var i = toDispose.Count - 1; i >= 0; i--)
        {
            if (toDispose[i] is IDisposable disposable)
            {
                disposable.Dispose();
            }
            else
            {
                // IAsyncDisposable のみ実装のサービスを同期 Dispose した場合は例外 (MEDI 互換)
                // Synchronous Dispose of a service implementing only IAsyncDisposable throws (MEDI compatible).
#pragma warning disable CA1065
                throw new InvalidOperationException(
                    $"'{toDispose[i].GetType()}' type only implements IAsyncDisposable. Use DisposeAsync to dispose the container.");
#pragma warning restore CA1065
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<object>? toDispose;
        lock (Sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            registry = ServiceRegistry.DisposedSentinel;
            toDispose = disposables;
            disposables = null;
        }

        if (toDispose is null)
        {
            return;
        }

        for (var i = toDispose.Count - 1; i >= 0; i--)
        {
            if (toDispose[i] is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                ((IDisposable)toDispose[i]).Dispose();
            }
        }
    }
}
