// NativeAOT 実行検証: 属性コンポーネント (生成経路) と実行時登録 (互換経路) の両方を PublishAot でビルドした実行ファイル上で検証する
// NativeAOT execution verification: exercises both attribute components (generated path) and runtime registrations (runtime path) in an executable built with PublishAot.
using BunnyTail.Resolver;
using BunnyTail.Resolver.AotTests;

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

// AddComponents = 属性コンポーネント (生成登録メソッド) / AddTransient = Add* 収集 → 生成ファクトリ
// AddComponents = attribute components (generated registration method) / AddTransient = Add* collection -> generated factory.
var services = new ServiceCollection()
    .AddComponents()
    .AddTransient<RuntimeRegistered>();

using (var provider = services.BuildResolverServiceProvider())
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
}

// disposal
DisposableAot disposable;
using (var provider = services.BuildResolverServiceProvider())
{
    disposable = provider.GetRequiredService<DisposableAot>();
}

Assert(disposable.Disposed, "singleton disposed with provider");

Console.WriteLine(failures == 0 ? "ALL OK" : $"FAILED: {failures}");
return failures == 0 ? 0 : 1;

namespace BunnyTail.Resolver.AotTests
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
