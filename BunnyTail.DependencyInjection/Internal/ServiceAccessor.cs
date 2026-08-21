namespace BunnyTail.DependencyInjection.Internal;

using System.Runtime.CompilerServices;

internal abstract class ServiceAccessor
{
#pragma warning disable SA1401
    public readonly ResultCache Cache;

    public readonly int Slot;
#pragma warning restore SA1401

    private readonly bool trackDisposable;

    private object? rootCached;

    protected ServiceAccessor(ResultCache cache, int slot, bool trackDisposable)
    {
        Cache = cache;
        Slot = slot;
        this.trackDisposable = trackDisposable;
    }

    public virtual object? GetValue(ServiceProviderScope scope)
    {
        // hot path 最短化: Transient は直接生成、Singleton はフィールド 1 読み、Scoped はスロット 1 読み。
        // 初回生成は NoInlining の cold path へ分離 (JIT-04)
        // Shortest hot path: transient creates directly, singleton is one field read, scoped is one slot read.
        // First-time creation is split into NoInlining cold paths (JIT-04).
        if (Cache == ResultCache.None)
        {
            var value = Create(scope);
            if (trackDisposable)
            {
                scope.CaptureDisposable(value);
            }

            return value;
        }

        if (Cache == ResultCache.Root)
        {
            var cached = rootCached;
            return cached is not null ? ServiceProviderScope.UnwrapSlotValue(cached) : CreateRoot(scope);
        }

        var existing = scope.GetSlot(Slot);
        return existing is not null ? ServiceProviderScope.UnwrapSlotValue(existing) : CreateScoped(scope);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private object? CreateRoot(ServiceProviderScope scope)
    {
        var storage = scope.RootScope;
        lock (storage.Sync)
        {
            var cached = rootCached;
            if (cached is not null)
            {
                return ServiceProviderScope.UnwrapSlotValue(cached);
            }

            storage.CheckDisposed();

            var value = Create(storage);
            if (trackDisposable)
            {
                storage.CaptureDisposableUnderLock(value);
            }

            Volatile.Write(ref rootCached, ServiceProviderScope.WrapSlotValue(value));
            return value;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private object? CreateScoped(ServiceProviderScope scope)
    {
        lock (scope.Sync)
        {
            var existing = scope.GetSlot(Slot);
            if (existing is not null)
            {
                return ServiceProviderScope.UnwrapSlotValue(existing);
            }

            scope.CheckDisposed();

            var value = Create(scope);
            if (trackDisposable)
            {
                scope.CaptureDisposableUnderLock(value);
            }

            scope.SetSlotUnderLock(Slot, value);
            return value;
        }
    }

    protected abstract object? Create(ServiceProviderScope scope);
}
