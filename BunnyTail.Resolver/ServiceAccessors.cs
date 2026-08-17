namespace BunnyTail.Resolver;

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

// 解決結果のキャッシュ位置
// Cache location of the resolved instance.
internal enum ResultCache
{
    None,      // Transient: 毎回生成 / created every time
    Root,      // Singleton: ルートスコープに保持 / held by the root scope
    Scoped // Scoped: 解決スコープに保持 / held by the resolving scope
}

// サービステーブルのエントリ。lifetime 管理と disposal 追跡は基底に集約し、派生は「インスタンス生成」だけを担う
// (生成経路と互換経路のセマンティクスを一致させるため)
// Service table entry. Lifetime management and disposal tracking are centralized in this base class and derived
// classes only create instances, which keeps the generated path and the runtime path semantically identical.
internal abstract class ServiceAccessor
{
#pragma warning disable SA1401
    public readonly ResultCache Cache;

    public readonly int Slot;
#pragma warning restore SA1401

    // disposal 追跡要否は accessor 構築時に実装型で確定する。実行時 is チェックの排除
    // (Sandbox の DisposalTrackingBenchmark)。実装型が不明なユーザーファクトリのみ true 固定
    // Whether disposal tracking is needed is settled from the implementation type when the accessor is built,
    // eliminating runtime type checks (see DisposalTrackingBenchmark in the sandbox).
    // Only user factories with unknown implementation types stay true.
    private readonly bool trackDisposable;

    // Singleton キャッシュ。accessor は ServiceRegistry ごと = プロバイダごとに固有なので、
    // スコープのスロット配列を経由せずここに保持できる。null 値は NullSentinel でラップする
    // Singleton cache. Accessors are unique per ServiceRegistry (= per provider), so the instance can be
    // held here without going through the scope slot array. Null values are wrapped with NullSentinel.
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

            // Singleton の依存はルートスコープ文脈で生成する (MEDI 互換: 子スコープから初回解決しても、
            // 注入される IServiceProvider はルートを指す)
            // Singleton dependencies are created in the root scope context (MEDI compatible: even when first
            // resolved from a child scope, the injected IServiceProvider points to the root).
            var value = Create(storage);
            if (trackDisposable)
            {
                storage.CaptureDisposableUnderLock(value);
            }

            // 構築完了後に release 公開。読み出し側はロックなしの素の読みでよい: .NET のメモリモデルは
            // 参照の release 公開後、参照経由の従属読みの順序を全対応アーキテクチャ (ARM64 含む) で保証する
            // Published with release semantics after construction completes. Plain lock-free reads are sufficient:
            // the .NET memory model guarantees ordering of dependent reads through the reference after a release
            // publication on all supported architectures, including ARM64.
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

// 定数 (ImplementationInstance)。外部所有のため dispose 追跡しない
// Constant (ImplementationInstance). Externally owned, so it is not tracked for disposal.
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

// 非 keyed ファクトリ (Func<IServiceProvider, object>)
// Non-keyed factory (Func<IServiceProvider, object>).
internal sealed class FactoryAccessor : ServiceAccessor
{
    // インライン展開前提の検証用に公開 (ServiceRegistry.InlinedDependenciesMatch が生成ファクトリと参照比較する)
    // Exposed for inline assumption validation (ServiceRegistry.InlinedDependenciesMatch compares it with the generated factory by reference).
    public Func<IServiceProvider, object> Factory { get; }

    public FactoryAccessor(Func<IServiceProvider, object> factory, ResultCache cache, int slot, bool trackDisposable)
        : base(cache, slot, trackDisposable)
    {
        Factory = factory;
    }

    protected override object Create(ServiceProviderScope scope) => Factory(scope);
}

// 依存を解決済み配列で受け取る生成ファクトリ (deps 形)。インスタンススロットは root 解決済みインスタンス、
// アクセサスロットは採用時に生成された DependencyAccessor (公開ハンドル) を保持する。
// deps は初回生成時に一度だけ充填する (lazy セマンティクス維持: singleton は初回解決まで生成されない)。
// 充填が競合しても、インスタンススロットはキャッシュ済みインスタンス、アクセサスロットは等価なハンドルに
// なるため、内容は同一で後勝ちで問題ない
// Generated factory receiving resolved dependencies as an array (deps shape). Instance slots hold root-resolved
// instances and accessor slots hold DependencyAccessor handles. The array is filled once on first creation,
// preserving lazy semantics (singletons are not created until first resolution). Concurrent fills are benign:
// instance slots yield the cached instance and accessor slots yield equivalent handles, so last-write wins.
internal sealed class DepsFactoryAccessor : ServiceAccessor
{
    // インライン展開前提の検証用に公開 (ServiceRegistry が生成ファクトリと参照比較する)
    // Exposed for inline assumption validation (ServiceRegistry compares it with the generated factory by reference).
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

// 依存を解決済み配列で受け取る keyed 生成ファクトリ (deps 形)。非 keyed の DepsFactoryAccessor と同じ充填規則
// Keyed generated factory receiving resolved dependencies as an array (deps shape), with the same fill rules as the non-keyed DepsFactoryAccessor.
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

// keyed ファクトリ (Func<IServiceProvider, object?, object>)
// Keyed factory (Func<IServiceProvider, object?, object>).
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

// コンストラクタ引数の解決計画
// Resolution plan for a constructor parameter.
internal sealed class ParameterPlan
{
    private readonly ServiceAccessor? accessor;

    private readonly object? constantValue;   // 既定値 or [ServiceKey] の注入値 / default value or the injected [ServiceKey] value

    private ParameterPlan(ServiceAccessor? accessor, object? constantValue, bool isServiceKey)
    {
        this.accessor = accessor;
        this.constantValue = constantValue;
        IsServiceKey = isServiceKey;
    }

    // サービス解決による計画か。生成ファクトリ採用可否の判定に使用 (既定値定数が混ざる場合、
    // 生成ファクトリの GetRequiredService とは挙動が変わるため不採用)
    // Whether this plan resolves a service. Used to decide generated factory adoption (plans containing
    // default value constants behave differently from GetRequiredService in the generated factory).
    public bool IsService => accessor is not null;

    // [ServiceKey] 注入か (keyed 生成ファクトリは key 引数で同じ値を受け取るため採用可)
    // Whether this is a [ServiceKey] injection (keyed generated factories receive the same value as the key argument, so adoption is allowed).
    public bool IsServiceKey { get; }

    public static ParameterPlan FromService(ServiceAccessor accessor) => new(accessor, null, false);

    public static ParameterPlan FromConstant(object? value) => new(null, value, false);

    public static ParameterPlan FromServiceKey(object? key) => new(null, key, true);

    public object? Resolve(ServiceProviderScope scope) => accessor is not null ? accessor.GetValue(scope) : constantValue;
}

// [Inject] プロパティ注入の計画 (インスタンス生成後に実行)
// Plan for [Inject] property injection (performed after the instance is constructed).
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
// Constructor invocation (runtime path: ConstructorInfo.Invoke based, no Emit).
internal sealed class ConstructorAccessor : ServiceAccessor
{
    // ConstructorInfo.Invoke より速い呼び出しスタブ (.NET 8+、Emit 不使用で NativeAOT でも動作)。
    // 例外は TargetInvocationException に包まれず素通しになる (生成経路と同じ挙動)
    // Faster invocation stub than ConstructorInfo.Invoke (.NET 8+, no Emit, works on NativeAOT).
    // Exceptions propagate unwrapped, matching the generated path.
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

        // [Inject] プロパティ注入 (生成経路と同セマンティクス: 生成後に設定)
        // [Inject] property injection (same semantics as the generated path: assigned after construction).
        for (var i = 0; i < properties.Length; i++)
        {
            properties[i].Property.SetValue(instance, properties[i].Plan.Resolve(scope));
        }

        // 初期化コールバック (生成経路と同セマンティクス: プロパティ注入の後。例外は素通し)
        // Initialization callback (same semantics as the generated path: after property injection, exceptions flow through unwrapped).
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

// 引数なし値型 (Activator 経由)
// Parameterless value type (through Activator).
internal sealed class ValueTypeAccessor : ServiceAccessor
{
    [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    private readonly Type type;

    private readonly bool initializable;

    public ValueTypeAccessor(
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type type,
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
            // box に対する呼び出しなので、box 内の値が初期化される
            // Invoked on the box, so the boxed value is what gets initialized.
            ((IInitializable)value!).Initialize();
        }

        return value;
    }
}

// IEnumerable<T> (T[] 実体、登録順)
// IEnumerable<T> (materialized as T[] in registration order).
internal sealed class EnumerableAccessor : ServiceAccessor
{
    // new T[n] を行う open generic メソッドの定義。デリゲート経由で静的参照し、トリミングで消えないようにする
    // Open generic definition of new T[n], acquired through a delegate so trimming preserves it.
    private static readonly MethodInfo CreateTypedArrayMethod = new Func<int, Array>(CreateTypedArray<object>).Method.GetGenericMethodDefinition();

    private readonly Type elementType;

    private readonly ServiceAccessor[] items;

    // 参照型要素のみ: new T[n] デリゲート (Array.CreateInstance の固定 ~50ns を回避。Sandbox の
    // EnumerableMaterializationBenchmark で確定)。値型要素は null で従来経路へフォールバック
    // Reference elements only: a new T[n] delegate avoiding the fixed ~50ns of Array.CreateInstance
    // (settled by EnumerableMaterializationBenchmark in the sandbox). Null for value type elements, which fall back.
    private readonly Func<int, Array>? arrayFactory;

    // 配列自体は追跡しない (要素は各 accessor が追跡する)
    // The array itself is not tracked (each element is tracked by its own accessor).
    public EnumerableAccessor(Type elementType, ServiceAccessor[] items, ResultCache cache, int slot)
        : base(cache, slot, trackDisposable: false)
    {
        this.elementType = elementType;
        this.items = items;
        arrayFactory = elementType.IsValueType ? null : CreateArrayFactory(elementType);
    }

    private static T[] CreateTypedArray<T>(int length) => new T[length];

    // 参照型引数は NativeAOT でも shared generic で動作する (Sandbox の AOT プローブで実測確認済み)。
    // 値型引数は実行時に失敗するため、呼び出し元が IsValueType で除外している
    // Reference type arguments work on NativeAOT through shared generics (confirmed by the sandbox AOT probe).
    // Value type arguments would fail at runtime, so the caller excludes them via IsValueType.
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "参照型要素のみ (shared generic で動作)。値型要素は Array.CreateInstance へフォールバックする")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "対象はこのクラス内の new T[n] のみで、型引数にメタデータ要求はない")]
    private static Func<int, Array> CreateArrayFactory(Type elementType) =>
        CreateTypedArrayMethod.MakeGenericMethod(elementType).CreateDelegate<Func<int, Array>>();

    // 要素型 T の配列は IEnumerable<T> の要求経路 (呼び出し側の型参照) でメタデータが保持される
    // Metadata of T[] is preserved through the IEnumerable<T> request path (the caller's type reference).
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "IEnumerable<T> が要求される要素型の配列型は参照経路で保持される (AotTests で検証済み)")]
    protected override object Create(ServiceProviderScope scope)
    {
        if (arrayFactory is not null)
        {
            // 参照型要素: 型付き配列 + 共変ビューへの直接格納 (Array.SetValue のリフレクション経路を回避)
            // Reference elements: typed array with direct stores through the covariant view (no Array.SetValue reflection).
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

// IServiceProvider (解決スコープ自身を返す)
// IServiceProvider (returns the resolving scope itself).
internal sealed class ServiceProviderAccessor : ServiceAccessor
{
    public ServiceProviderAccessor()
        : base(ResultCache.None, -1, trackDisposable: false)
    {
    }

    public override object GetValue(ServiceProviderScope scope) => scope;

    protected override object Create(ServiceProviderScope scope) => scope;
}
