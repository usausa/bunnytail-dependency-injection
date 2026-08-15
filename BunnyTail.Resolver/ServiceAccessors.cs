namespace BunnyTail.Resolver;

using System.Reflection;
using System.Runtime.ExceptionServices;

// 解決結果のキャッシュ位置
internal enum ResultCache
{
    None,      // Transient: 毎回生成
    Root,      // Singleton: ルートスコープのスロット
    Scoped,    // Scoped: 解決スコープのスロット
}

// サービステーブルのエントリ。lifetime 管理 + disposal 追跡は基底に集約し、
// 派生は「インスタンス生成」だけを担う (生成経路/互換経路のセマンティクス一致のため)
internal abstract class ServiceAccessor
{
#pragma warning disable SA1401
    public readonly ResultCache Cache;

    public readonly int Slot;
#pragma warning restore SA1401

    protected ServiceAccessor(ResultCache cache, int slot)
    {
        Cache = cache;
        Slot = slot;
    }

    protected virtual bool TrackDisposable => true;

    public virtual object? GetValue(ServiceProviderScope scope)
    {
        switch (Cache)
        {
            case ResultCache.Root:
                return GetOrCreate(scope.RootScope, scope);
            case ResultCache.Scoped:
                return GetOrCreate(scope, scope);
            default:
                var value = Create(scope);
                if (TrackDisposable)
                {
                    scope.CaptureDisposable(value);
                }

                return value;
        }
    }

    private object? GetOrCreate(ServiceProviderScope storage, ServiceProviderScope resolveScope)
    {
        _ = resolveScope;

        var existing = storage.GetSlot(Slot);
        if (existing is not null)
        {
            return ServiceProviderScope.UnwrapSlotValue(existing);
        }

        lock (storage.Sync)
        {
            existing = storage.GetSlot(Slot);
            if (existing is not null)
            {
                return ServiceProviderScope.UnwrapSlotValue(existing);
            }

            storage.CheckDisposed();

            // Singleton の依存はルートスコープ文脈で生成する (MEDI 互換:
            // 子スコープから初回解決しても、注入される IServiceProvider はルートを指す)
            var value = Create(storage);
            if (TrackDisposable)
            {
                storage.CaptureDisposableUnderLock(value);
            }

            storage.SetSlotUnderLock(Slot, value);
            return value;
        }
    }

    protected abstract object? Create(ServiceProviderScope scope);
}

// 定数 (ImplementationInstance)。外部所有のため dispose 追跡しない
internal sealed class ConstantAccessor : ServiceAccessor
{
    private readonly object? value;

    public ConstantAccessor(object? value)
        : base(ResultCache.None, -1)
    {
        this.value = value;
    }

    public override object? GetValue(ServiceProviderScope scope) => value;

    protected override object? Create(ServiceProviderScope scope) => value;
}

// 非 keyed ファクトリ (Func<IServiceProvider, object>)
internal sealed class FactoryAccessor : ServiceAccessor
{
    private readonly Func<IServiceProvider, object> factory;

    public FactoryAccessor(Func<IServiceProvider, object> factory, ResultCache cache, int slot)
        : base(cache, slot)
    {
        this.factory = factory;
    }

    protected override object? Create(ServiceProviderScope scope) => factory(scope);
}

// keyed ファクトリ (Func<IServiceProvider, object?, object>)
internal sealed class KeyedFactoryAccessor : ServiceAccessor
{
    private readonly Func<IServiceProvider, object?, object> factory;

    private readonly object? key;

    public KeyedFactoryAccessor(Func<IServiceProvider, object?, object> factory, object? key, ResultCache cache, int slot)
        : base(cache, slot)
    {
        this.factory = factory;
        this.key = key;
    }

    protected override object? Create(ServiceProviderScope scope) => factory(scope, key);
}

// コンストラクタ引数の解決計画
internal sealed class ParameterPlan
{
    private readonly ServiceAccessor? accessor;

    private readonly object? constantValue;   // 既定値 or [ServiceKey] の注入値

    private ParameterPlan(ServiceAccessor? accessor, object? constantValue, bool isServiceKey)
    {
        this.accessor = accessor;
        this.constantValue = constantValue;
        IsServiceKey = isServiceKey;
    }

    // サービス解決による計画か (生成ファクトリ採用可否の判定に使用:
    // 既定値定数が混ざる場合、生成ファクトリの GetRequiredService とは挙動が変わるため不採用)
    public bool IsService => accessor is not null;

    // [ServiceKey] 注入か (keyed 生成ファクトリは key 引数で同じ値を受け取るため採用可)
    public bool IsServiceKey { get; }

    public static ParameterPlan FromService(ServiceAccessor accessor) => new(accessor, null, false);

    public static ParameterPlan FromConstant(object? value) => new(null, value, false);

    public static ParameterPlan FromServiceKey(object? key) => new(null, key, true);

    public object? Resolve(ServiceProviderScope scope) => accessor is not null ? accessor.GetValue(scope) : constantValue;
}

// [Inject] プロパティ注入の計画 (インスタンス生成後に実行)
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

// コンストラクタ呼び出し (互換経路: ConstructorInfo.Invoke ベース、Emit 不使用)
internal sealed class ConstructorAccessor : ServiceAccessor
{
    private readonly ConstructorInfo constructor;

    private readonly ParameterPlan[] plans;

    private readonly PropertyInjection[] properties;

    public ConstructorAccessor(ConstructorInfo constructor, ParameterPlan[] plans, PropertyInjection[] properties, ResultCache cache, int slot)
        : base(cache, slot)
    {
        this.constructor = constructor;
        this.plans = plans;
        this.properties = properties;
    }

    protected override object? Create(ServiceProviderScope scope)
    {
        var arguments = plans.Length == 0 ? [] : new object?[plans.Length];
        for (var i = 0; i < plans.Length; i++)
        {
            arguments[i] = plans[i].Resolve(scope);
        }

        object instance;
        try
        {
            instance = constructor.Invoke(arguments);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }

        // [Inject] プロパティ注入 (生成経路と同セマンティクス: 生成後に設定)
        for (var i = 0; i < properties.Length; i++)
        {
            properties[i].Property.SetValue(instance, properties[i].Plan.Resolve(scope));
        }

        return instance;
    }
}

// 引数なし値型 (Activator 経由)
internal sealed class ValueTypeAccessor : ServiceAccessor
{
    [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    private readonly Type type;

    public ValueTypeAccessor(
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type type,
        ResultCache cache,
        int slot)
        : base(cache, slot)
    {
        this.type = type;
    }

    protected override object? Create(ServiceProviderScope scope) => Activator.CreateInstance(type);
}

// IEnumerable<T> (T[] 実体、登録順)
internal sealed class EnumerableAccessor : ServiceAccessor
{
    private readonly Type elementType;

    private readonly ServiceAccessor[] items;

    public EnumerableAccessor(Type elementType, ServiceAccessor[] items, ResultCache cache, int slot)
        : base(cache, slot)
    {
        this.elementType = elementType;
        this.items = items;
    }

    // 配列自体は追跡しない (要素は各 accessor が追跡する)
    protected override bool TrackDisposable => false;

    // 要素型 T の配列は IEnumerable<T> の要求経路 (呼び出し側の型参照) でメタデータが保持される
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "IEnumerable<T> が要求される要素型の配列型は参照経路で保持される (AotTests で検証済み)")]
    protected override object Create(ServiceProviderScope scope)
    {
        var array = Array.CreateInstance(elementType, items.Length);
        for (var i = 0; i < items.Length; i++)
        {
            array.SetValue(items[i].GetValue(scope), i);
        }

        return array;
    }
}

// IServiceProvider (解決スコープ自身を返す)
internal sealed class ServiceProviderAccessor : ServiceAccessor
{
    public ServiceProviderAccessor()
        : base(ResultCache.None, -1)
    {
    }

    public override object GetValue(ServiceProviderScope scope) => scope;

    protected override object Create(ServiceProviderScope scope) => scope;
}
