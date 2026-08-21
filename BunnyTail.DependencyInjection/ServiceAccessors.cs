namespace BunnyTail.DependencyInjection;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

//--------------------------------------------------------------------------------
// Accessor
//--------------------------------------------------------------------------------

internal enum ResultCache
{
    // Transient: Created every
    None,
    // Singleton: Held by root scope
    Root,
    // Scoped: Held by resolving scope
    Scoped
}

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

//--------------------------------------------------------------------------------
// Constant
//--------------------------------------------------------------------------------

internal sealed class ConstantAccessor : ServiceAccessor
{
    private readonly object? value;

    public ConstantAccessor(object? value)
        : base(ResultCache.None, -1, trackDisposable: false)
    {
        this.value = value;
    }

    public override object? GetValue(ServiceProviderScope scope) => value;

    protected override object? Create(ServiceProviderScope scope) => value;
}

//--------------------------------------------------------------------------------
// Factory
//--------------------------------------------------------------------------------

internal sealed class FactoryAccessor : ServiceAccessor
{
    public Func<IServiceProvider, object> Factory { get; }

    public FactoryAccessor(Func<IServiceProvider, object> factory, ResultCache cache, int slot, bool trackDisposable)
        : base(cache, slot, trackDisposable)
    {
        Factory = factory;
    }

    protected override object Create(ServiceProviderScope scope) => Factory(scope);
}

//--------------------------------------------------------------------------------
// Dependency array factory
//--------------------------------------------------------------------------------

internal sealed class DepsFactoryAccessor : ServiceAccessor
{
    public Func<IServiceProvider, object?[], object> Factory { get; }

    private readonly ServiceAccessor[] dependencyAccessors;

    private readonly DependencyAccessor?[] dependencyHandles;

    private object?[]? resolved;

    public DepsFactoryAccessor(Func<IServiceProvider, object?[], object> factory, ServiceAccessor[] dependencyAccessors, DependencyAccessor?[] dependencyHandles, ResultCache cache, int slot, bool trackDisposable)
        : base(cache, slot, trackDisposable)
    {
        Factory = factory;
        this.dependencyAccessors = dependencyAccessors;
        this.dependencyHandles = dependencyHandles;
    }

    protected override object Create(ServiceProviderScope scope)
    {
        var deps = resolved ?? FillDependencies(scope);
        return Factory(scope, deps);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private object?[] FillDependencies(ServiceProviderScope scope)
    {
        var array = new object?[dependencyAccessors.Length];
        for (var i = 0; i < dependencyAccessors.Length; i++)
        {
            array[i] = dependencyHandles[i] ?? dependencyAccessors[i].GetValue(scope.RootScope);
        }

        Volatile.Write(ref resolved, array);
        return array;
    }
}

//--------------------------------------------------------------------------------
// Keyed factory
//--------------------------------------------------------------------------------

internal sealed class KeyedFactoryAccessor : ServiceAccessor
{
    private readonly Func<IServiceProvider, object?, object> factory;

    private readonly object? key;

    public KeyedFactoryAccessor(Func<IServiceProvider, object?, object> factory, object? key, ResultCache cache, int slot, bool trackDisposable)
        : base(cache, slot, trackDisposable)
    {
        this.factory = factory;
        this.key = key;
    }

    protected override object Create(ServiceProviderScope scope) => factory(scope, key);
}

//--------------------------------------------------------------------------------
// Keyed dependency array factory
//--------------------------------------------------------------------------------

internal sealed class KeyedDepsFactoryAccessor : ServiceAccessor
{
    private readonly Func<IServiceProvider, object?, object?[], object> factory;

    private readonly object? key;

    private readonly ServiceAccessor[] dependencyAccessors;

    private readonly DependencyAccessor?[] dependencyHandles;

    private object?[]? resolved;

    public KeyedDepsFactoryAccessor(Func<IServiceProvider, object?, object?[], object> factory, object? key, ServiceAccessor[] dependencyAccessors, DependencyAccessor?[] dependencyHandles, ResultCache cache, int slot, bool trackDisposable)
        : base(cache, slot, trackDisposable)
    {
        this.factory = factory;
        this.key = key;
        this.dependencyAccessors = dependencyAccessors;
        this.dependencyHandles = dependencyHandles;
    }

    protected override object Create(ServiceProviderScope scope)
    {
        var deps = resolved ?? FillDependencies(scope);
        return factory(scope, key, deps);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private object?[] FillDependencies(ServiceProviderScope scope)
    {
        var array = new object?[dependencyAccessors.Length];
        for (var i = 0; i < dependencyAccessors.Length; i++)
        {
            array[i] = dependencyHandles[i] ?? dependencyAccessors[i].GetValue(scope.RootScope);
        }

        Volatile.Write(ref resolved, array);
        return array;
    }
}

//--------------------------------------------------------------------------------
// Constructor injection
//--------------------------------------------------------------------------------

internal sealed class ParameterPlan
{
    private readonly ServiceAccessor? accessor;

    private readonly object? constantValue;

    private ParameterPlan(ServiceAccessor? accessor, object? constantValue, bool isServiceKey)
    {
        this.accessor = accessor;
        this.constantValue = constantValue;
        IsServiceKey = isServiceKey;
    }

    public bool IsService => accessor is not null;

    public bool IsServiceKey { get; }

    public static ParameterPlan FromService(ServiceAccessor accessor) => new(accessor, null, false);

    public static ParameterPlan FromConstant(object? value) => new(null, value, false);

    public static ParameterPlan FromServiceKey(object? key) => new(null, key, true);

    public object? Resolve(ServiceProviderScope scope) => accessor is not null ? accessor.GetValue(scope) : constantValue;
}

internal sealed class ConstructorAccessor : ServiceAccessor
{
    private readonly ConstructorInvoker invoker;

    private readonly ParameterPlan[] plans;

    private readonly PropertyInjection[] properties;

    private readonly MethodInfo? postConstruct;

    private readonly bool initializable;

    public ConstructorAccessor(ConstructorInfo constructor, ParameterPlan[] plans, PropertyInjection[] properties, MethodInfo? postConstruct, bool initializable, ResultCache cache, int slot, bool trackDisposable)
        : base(cache, slot, trackDisposable)
    {
        invoker = ConstructorInvoker.Create(constructor);
        this.plans = plans;
        this.properties = properties;
        this.postConstruct = postConstruct;
        this.initializable = initializable;
    }

    protected override object Create(ServiceProviderScope scope)
    {
        object instance;
        if (plans.Length == 0)
        {
            instance = invoker.Invoke();
        }
        else
        {
            var arguments = new object?[plans.Length];
            for (var i = 0; i < plans.Length; i++)
            {
                arguments[i] = plans[i].Resolve(scope);
            }

            instance = invoker.Invoke(arguments.AsSpan());
        }

        // Property injection
        for (var i = 0; i < properties.Length; i++)
        {
            properties[i].Property.SetValue(instance, properties[i].Plan.Resolve(scope));
        }

        // Initialization
        if (postConstruct is not null)
        {
            try
            {
                postConstruct.Invoke(instance, null);
            }
            catch (TargetInvocationException e) when (e.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }
        else if (initializable)
        {
            ((IInitializable)instance).Initialize();
        }

        return instance;
    }
}

//--------------------------------------------------------------------------------
// Value type
//--------------------------------------------------------------------------------

internal sealed class ValueTypeAccessor : ServiceAccessor
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    private readonly Type type;

    private readonly bool initializable;

    public ValueTypeAccessor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type type,
        bool initializable,
        ResultCache cache,
        int slot,
        bool trackDisposable)
        : base(cache, slot, trackDisposable)
    {
        this.type = type;
        this.initializable = initializable;
    }

    protected override object? Create(ServiceProviderScope scope)
    {
        var value = Activator.CreateInstance(type);
        if (initializable)
        {
            ((IInitializable)value!).Initialize();
        }

        return value;
    }
}

//--------------------------------------------------------------------------------
// Enumerable
//--------------------------------------------------------------------------------

internal sealed class EnumerableAccessor : ServiceAccessor
{
    private static readonly MethodInfo CreateTypedArrayMethod = new Func<int, Array>(CreateTypedArray<object>).Method.GetGenericMethodDefinition();

    private readonly Type elementType;

    private readonly ServiceAccessor[] items;

    private readonly Func<int, Array>? arrayFactory;

    public EnumerableAccessor(Type elementType, ServiceAccessor[] items, ResultCache cache, int slot)
        : base(cache, slot, trackDisposable: false)
    {
        this.elementType = elementType;
        this.items = items;
        arrayFactory = elementType.IsValueType ? null : CreateArrayFactory(elementType);
    }

    private static T[] CreateTypedArray<T>(int length) => new T[length];

    [UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "Reference type elements only, which run through shared generics; value type elements fall back to Array.CreateInstance.")]
    [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "The only target is new T[n] inside this class, and the type argument carries no metadata requirement.")]
    private static Func<int, Array> CreateArrayFactory(Type elementType) =>
        CreateTypedArrayMethod.MakeGenericMethod(elementType).CreateDelegate<Func<int, Array>>();

    [UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "Array types of element types requested through IEnumerable<T> are preserved by the reference path, as verified by AotTests.")]
    protected override object Create(ServiceProviderScope scope)
    {
        if (arrayFactory is not null)
        {
            var typed = arrayFactory(items.Length);
            var view = (object?[])typed;
            for (var i = 0; i < items.Length; i++)
            {
                view[i] = items[i].GetValue(scope);
            }

            return typed;
        }

        var array = Array.CreateInstance(elementType, items.Length);
        for (var i = 0; i < items.Length; i++)
        {
            array.SetValue(items[i].GetValue(scope), i);
        }

        return array;
    }
}

//--------------------------------------------------------------------------------
// IServiceProvider
//--------------------------------------------------------------------------------

internal sealed class ServiceProviderAccessor : ServiceAccessor
{
    public ServiceProviderAccessor()
        : base(ResultCache.None, -1, trackDisposable: false)
    {
    }

    public override object GetValue(ServiceProviderScope scope) => scope;

    protected override object Create(ServiceProviderScope scope) => scope;
}

//--------------------------------------------------------------------------------
// Property injection
//--------------------------------------------------------------------------------

internal readonly struct PropertyInjection
{
#pragma warning disable SA1401
    public readonly PropertyInfo Property;

    public readonly ParameterPlan Plan;
#pragma warning restore SA1401

    public PropertyInjection(PropertyInfo property, ParameterPlan plan)
    {
        Property = property;
        Plan = plan;
    }
}
