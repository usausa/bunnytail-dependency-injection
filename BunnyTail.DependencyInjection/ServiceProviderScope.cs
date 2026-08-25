namespace BunnyTail.DependencyInjection;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using BunnyTail.DependencyInjection.Internal;

using Microsoft.Extensions.DependencyInjection;

// ReSharper disable RedundantExtendsListEntry
public sealed class ServiceProviderScope :
    IServiceScope,
    IServiceProvider,
    IKeyedServiceProvider,
    ISupportRequiredService,
    ITypeActivator,
    IDisposable,
    IAsyncDisposable
{
    private static readonly object NullSentinel = new();

    private readonly GeneratedServiceProvider provider;

    private ServiceRegistry registry;

    private object?[] slots = [];

    private List<object>? disposables;

    private bool disposed;

    internal bool IsRootScope { get; }

    internal ServiceProviderScope RootScope => IsRootScope ? this : provider.RootScope;

    internal object Sync { get; } = new();

    public IServiceProvider ServiceProvider => this;

    internal ServiceProviderScope(GeneratedServiceProvider provider, bool isRootScope)
    {
        this.provider = provider;
        registry = provider.Registry;
        IsRootScope = isRootScope;
    }

    //--------------------------------------------------------------------------------
    // Resolve
    //--------------------------------------------------------------------------------

    public object? GetService(Type serviceType) => registry.ResolveType(serviceType, this);

    public object GetRequiredService(Type serviceType)
    {
        var service = GetService(serviceType);
        if (service is null)
        {
            ThrowNoService(serviceType);
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
            ThrowAnyKeyNotEnumerable();
        }

        return registry.ResolveKeyed(serviceType, serviceKey, this);
    }

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
    {
        var service = GetKeyedService(serviceType, serviceKey);
        if (service is null)
        {
            ThrowNoKeyedService(serviceType, serviceKey);
        }

        return service;
    }

    private static bool IsEnumerableService(Type serviceType) =>
        serviceType.IsConstructedGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>);

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNoService(Type serviceType) =>
        throw new InvalidOperationException($"No service for type '{serviceType}' has been registered.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNoKeyedService(Type serviceType, object? serviceKey)
    {
        throw serviceKey is null
            ? new InvalidOperationException($"No service for type '{serviceType}' has been registered.")
            : new InvalidOperationException($"No service for type '{serviceType}' and service key '{serviceKey}' has been registered.");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowAnyKeyNotEnumerable() =>
        throw new InvalidOperationException("KeyedService.AnyKey can only be used to retrieve an IEnumerable of keyed services.");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? GetService<T>() => (T?)GetService(typeof(T));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetRequiredService<T>() => (T)GetRequiredService(typeof(T));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? GetKeyedService<T>(object? serviceKey) => (T?)GetKeyedService(typeof(T), serviceKey);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetRequiredKeyedService<T>(object? serviceKey) => (T)GetRequiredKeyedService(typeof(T), serviceKey);

    //--------------------------------------------------------------------------------
    // ITypeActivator
    //--------------------------------------------------------------------------------

    public object Activate(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type)
    {
        CheckDisposed();
        return registry.Activate(type, this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Activate<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class
        => (T)Activate(typeof(T));

    //--------------------------------------------------------------------------------
    // Slot storage
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
            if (value is IDisposable d)
            {
                d.Dispose();
            }

            throw new ObjectDisposedException(nameof(IServiceProvider));
        }

        (disposables ??= []).Add(value);
    }

    //--------------------------------------------------------------------------------
    // Dispose
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
#pragma warning disable CA1065
                throw new InvalidOperationException($"'{toDispose[i].GetType()}' type only implements IAsyncDisposable. Use DisposeAsync to dispose the container.");
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
// ReSharper restore RedundantExtendsListEntry
