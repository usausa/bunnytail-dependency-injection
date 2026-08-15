namespace BunnyTail.Resolver;

using Microsoft.Extensions.DependencyInjection;

// スコープ。ルートプロバイダも root スコープとして同じ実装を使う。
// 注入される IServiceProvider はこのスコープ自身 (MEDI 互換)
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

    private object?[] slots = [];

    private List<object>? disposables;

    private bool disposed;

    internal ServiceProviderScope(ResolverServiceProvider provider, bool isRootScope)
    {
        this.provider = provider;
        IsRootScope = isRootScope;
    }

    internal bool IsRootScope { get; }

    internal ServiceProviderScope RootScope => IsRootScope ? this : provider.RootScope;

    internal object Sync { get; } = new();

    public IServiceProvider ServiceProvider => this;

    //--------------------------------------------------------------------------------
    // Resolve
    //--------------------------------------------------------------------------------

    public object? GetService(Type serviceType)
    {
        CheckDisposed();
        return provider.ResolveService(new ServiceIdentifier(serviceType, null), this);
    }

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
        CheckDisposed();
        if (serviceKey is null)
        {
            return GetService(serviceType);
        }

        if (ReferenceEquals(serviceKey, KeyedService.AnyKey) && !IsEnumerableService(serviceType))
        {
            throw new InvalidOperationException("KeyedService.AnyKey can only be used to retrieve an IEnumerable of keyed services.");
        }

        return provider.ResolveService(new ServiceIdentifier(serviceType, serviceKey), this);
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

    //--------------------------------------------------------------------------------
    // Slot storage
    //--------------------------------------------------------------------------------

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

        array[index] = value ?? NullSentinel;
    }

    //--------------------------------------------------------------------------------
    // Disposal tracking
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
            if (value is IDisposable d)
            {
                d.Dispose();
            }

            throw new ObjectDisposedException(nameof(IServiceProvider));
        }

        (disposables ??= []).Add(value);
    }

    //--------------------------------------------------------------------------------
    // Dispose (生成の逆順 = LIFO)
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
                // MEDI 互換: IAsyncDisposable のみ実装のサービスを同期 Dispose した場合は例外
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
