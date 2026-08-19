// NativeAOT 実行検証: 属性コンポーネント (生成経路) と実行時登録 (互換経路) の両方を PublishAot でビルドした実行ファイル上で検証する
// NativeAOT execution verification: exercises both attribute components (generated path) and runtime registrations (runtime path) in an executable built with PublishAot.
using BunnyTail.DependencyInjection;
using BunnyTail.DependencyInjection.AotTests;

using Microsoft.Extensions.DependencyInjection;

var failures = 0;

void Assert(bool condition, string name)
{
    if (condition)
    {
        Console.WriteLine($"OK   : {name}");
    }
    else
    {
        Console.WriteLine($"FAIL : {name}");
        failures++;
    }
}

// AddAllGeneratedComponents = 属性コンポーネントの一括登録 (モジュール集約込み) / AddTransient = Add* 収集 → 生成ファクトリ
// AddAllGeneratedComponents = attribute components in one call (module aggregation included) / AddTransient = Add* collection -> generated factory.
var services = new ServiceCollection()
    .AddAllGeneratedComponents()
    .AddTransient<RuntimeRegistered>()
    .AddTransient(typeof(IAotGeneric<>), typeof(AotGeneric<>));

using (var provider = services.BuildGeneratedServiceProvider())
{
    // Singleton 同一性 / singleton identity
    var s1 = provider.GetRequiredService<AotSingleton>();
    var s2 = provider.GetRequiredService<AotSingleton>();
    Assert(ReferenceEquals(s1, s2), "singleton identity");

    // Transient + コンストラクタ注入 + [Inject] プロパティ注入 / transient + constructor injection + [Inject] property injection
    var t1 = provider.GetRequiredService<AotTransient>();
    var t2 = provider.GetRequiredService<AotTransient>();
    Assert(!ReferenceEquals(t1, t2), "transient distinct");
    Assert(ReferenceEquals(t1.Singleton, s1), "constructor injection");
    // 注釈上は非 null だが、注入前は default! なので「注入されたか」を実際に確認する意味がある
    // The annotation says non-null, yet the value is default! before injection, so checking it verifies injection actually happened.
    // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
    Assert(t1.Prop is not null, "inject property");

    // Scoped
    using (var scope = provider.CreateScope())
    {
        var sc1 = scope.ServiceProvider.GetRequiredService<IAotScoped>();
        var sc2 = scope.ServiceProvider.GetRequiredService<IAotScoped>();
        Assert(ReferenceEquals(sc1, sc2), "scoped identity in scope");
        Assert(ReferenceEquals(sc1, scope.ServiceProvider.GetRequiredService<AotScoped>()), "forwarded interface identity");
    }

    // transient グラフ (インライン展開ファクトリ) / transient graph (inlined factory)
    var g1 = provider.GetRequiredService<AotGraphRoot>();
    var g2 = provider.GetRequiredService<AotGraphRoot>();
    Assert(!ReferenceEquals(g1, g2), "graph transient distinct");
    Assert(!ReferenceEquals(g1.B.Dep, g1.C.Dep), "graph fresh per use site");

    // keyed ([ServiceKey] 注入込み) / keyed (including [ServiceKey] injection)
    var keyed = provider.GetRequiredKeyedService<IAotKeyed>("primary");
    Assert(keyed is AotKeyed { Key: "primary" }, "keyed with ServiceKey");

    // IEnumerable
    var all = provider.GetServices<IAotMulti>().ToArray();
    Assert(all.Length == 2, "enumerable count");

    // 実行時登録 (互換経路/収集ファクトリ) / runtime registration (runtime path / collected factory)
    Assert(provider.GetService<RuntimeRegistered>() is not null, "runtime registered");

    // 初期化コールバック / initialization callbacks
    Assert(provider.GetRequiredService<AotPostConstruct>().Initialized, "post construct method");
    Assert(provider.GetRequiredService<AotInitializable>().Initialized, "initializable interface");

    // open generic の閉型 (値型引数は生成ファクトリでのみ AOT 安全になる)。Type ベース経路を意図的に使う
    // (typeof は閉型 usage の収集元も兼ねる)
    // Closed forms of an open generic (value type arguments are AOT safe only through the generated factory).
    // The Type based path is intentional, and the typeof expressions double as the collected closed usages.
    var closedReference = typeof(IAotGeneric<string>);
    var closedValueType = typeof(IAotGeneric<int>);
    Assert(provider.GetService(closedReference) is AotGeneric<string>, "open generic closed reference");
    Assert(provider.GetService(closedValueType) is AotGeneric<int>, "open generic closed value type");

    // typeof の出現なし: コンストラクタ依存だけから発見された閉型 (依存駆動の発見)
    // No typeof usage: the closed form discovered from the constructor dependency alone (dependency driven discovery).
    Assert(provider.GetRequiredService<AotGenericConsumer>().Value is AotGeneric<double>, "open generic closed by dependency");
}

// disposal
DisposableAot disposable;
using (var provider = services.BuildGeneratedServiceProvider())
{
    disposable = provider.GetRequiredService<DisposableAot>();
}

Assert(disposable.Disposed, "singleton disposed with provider");

Console.WriteLine(failures == 0 ? "ALL OK" : $"FAILED: {failures}");
return failures == 0 ? 0 : 1;

namespace BunnyTail.DependencyInjection.AotTests
{
    public interface IAotScoped;

    public interface IAotKeyed;

    public interface IAotMulti;

    [Singleton]
    public sealed class AotSingleton;

    [Singleton]
    public sealed class AotPropDependency;

    [Transient]
    public sealed class AotTransient(AotSingleton singleton)
    {
        public AotSingleton Singleton { get; } = singleton;

        [Inject]
        public AotPropDependency Prop { get; set; } = default!;
    }

    [Scoped]
    public sealed class AotScoped : IAotScoped;

    [Singleton(PostConstruct = nameof(Setup))]
    public sealed class AotPostConstruct
    {
        public bool Initialized { get; private set; }

        public void Setup() => Initialized = true;
    }

    [Transient]
    public sealed class AotInitializable : IInitializable
    {
        public bool Initialized { get; private set; }

        public void Initialize() => Initialized = true;
    }

    // open generic 登録の検証用マーカー。型引数は登録形状のためだけに必要
    // Marker for verifying open generic registration; the type parameter exists only to shape the registration.
    // ReSharper disable once UnusedTypeParameter
    public interface IAotGeneric<T>;

    public sealed class AotGeneric<T> : IAotGeneric<T>;

    // IAotGeneric<double> は typeof で一度も書かれず、この ctor 依存だけが出現箇所
    // IAotGeneric<double> is never written in a typeof; this constructor dependency is its only occurrence.
    [Transient]
    public sealed class AotGenericConsumer(IAotGeneric<double> value)
    {
        public IAotGeneric<double> Value { get; } = value;
    }

    [Transient]
    public sealed class AotGraphDep;

    [Transient]
    public sealed class AotGraphB(AotGraphDep dep)
    {
        public AotGraphDep Dep { get; } = dep;
    }

    [Transient]
    public sealed class AotGraphC(AotGraphDep dep)
    {
        public AotGraphDep Dep { get; } = dep;
    }

    [Transient]
    public sealed class AotGraphRoot(AotGraphB b, AotGraphC c)
    {
        public AotGraphB B { get; } = b;

        public AotGraphC C { get; } = c;
    }

    [Singleton(As = typeof(IAotKeyed), Key = "primary")]
    public sealed class AotKeyed([ServiceKey] string key) : IAotKeyed
    {
        public string Key { get; } = key;
    }

    [Singleton(As = typeof(IAotMulti))]
    public sealed class AotMulti1 : IAotMulti;

    [Singleton(As = typeof(IAotMulti))]
    public sealed class AotMulti2 : IAotMulti;

    [Singleton]
    public sealed class DisposableAot : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    public sealed class RuntimeRegistered;
}
