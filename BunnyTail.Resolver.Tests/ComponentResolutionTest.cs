namespace BunnyTail.Resolver.Tests;

using BunnyTail.Resolver;
using BunnyTail.Resolver.Tests.Components;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

// 属性コンポーネント (生成された AddComponents + 生成ファクトリ) の機能検証
// Functional verification of attribute components (generated AddComponents + generated factories).
public sealed class ComponentResolutionTest
{
    private static ResolverServiceProvider CreateProvider() =>
        new ServiceCollection().AddComponents().BuildResolverServiceProvider();

    [Fact]
    public void SingletonIsSameAcrossScopes()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var root = provider.GetRequiredService<SingletonComponent>();
        var scoped = scope.ServiceProvider.GetRequiredService<SingletonComponent>();

        Assert.Same(root, scoped);
    }

    [Fact]
    public void TransientIsDistinct()
    {
        using var provider = CreateProvider();

        var a = provider.GetRequiredService<TransientComponent>();
        var b = provider.GetRequiredService<TransientComponent>();

        Assert.NotSame(a, b);
        Assert.Same(a.Singleton, b.Singleton);
    }

    [Fact]
    public void InjectPropertyIsInjected()
    {
        using var provider = CreateProvider();

        var a = provider.GetRequiredService<TransientComponent>();
        var b = provider.GetRequiredService<TransientComponent>();

        Assert.NotNull(a.Prop);
        Assert.Same(a.Prop, b.Prop);   // PropDependency は Singleton / PropDependency is a singleton
        Assert.Same(a.Prop, provider.GetRequiredService<PropDependency>());
    }

    [Fact]
    public void ScopedIsPerScopeAndForwardedInterfaceIsSameInstance()
    {
        using var provider = CreateProvider();
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var byClass = scope1.ServiceProvider.GetRequiredService<ScopedComponent>();
        var byInterface = scope1.ServiceProvider.GetRequiredService<IScopedService>();
        var other = scope2.ServiceProvider.GetRequiredService<IScopedService>();

        Assert.Same(byClass, byInterface);   // フォワーディング登録で同一インスタンス / same instance through the forwarding registration
        Assert.NotSame(byInterface, other);  // スコープごとに別 / distinct per scope
    }

    [Fact]
    public void KeyedComponentIsResolved()
    {
        using var provider = CreateProvider();

        var service = provider.GetRequiredKeyedService<IKeyedService>("primary");

        Assert.IsType<PrimaryKeyedComponent>(service);
        Assert.Null(provider.GetService<IKeyedService>());   // 非 keyed では解決されない / not resolvable without a key
    }

    [Fact]
    public void SingletonIsDisposedWithProvider()
    {
        DisposableSingleton singleton;
        using (var provider = CreateProvider())
        {
            singleton = provider.GetRequiredService<DisposableSingleton>();
            Assert.False(singleton.Disposed);
        }

        Assert.True(singleton.Disposed);
    }

    // ---- transient グラフのインライン展開 / inline expansion of transient graphs ----

    [Fact]
    public void TransientGraphDependenciesAreFreshPerUseSite()
    {
        using var provider = CreateProvider();

        var root = provider.GetRequiredService<GraphRoot>();

        // MEDI 互換: 同一 transient 依存も使用箇所ごとに新規生成 (インスタンス共有しない)
        // MEDI compatible: the same transient dependency is created fresh at every use site (never shared).
        Assert.NotSame(root.A.Leaf, root.B.Leaf);

        var other = provider.GetRequiredService<GraphRoot>();
        Assert.NotSame(root, other);
        Assert.NotSame(root.A, other.A);
        Assert.NotSame(root.A.Leaf, other.A.Leaf);
    }

    [Fact]
    public void DisposableTransientDependencyIsTrackedByScope()
    {
        using var provider = CreateProvider();

        DisposableLeaf leaf;
        using (var scope = provider.CreateScope())
        {
            leaf = scope.ServiceProvider.GetRequiredService<NodeWithDisposable>().Leaf;
            Assert.False(leaf.Disposed);
        }

        Assert.True(leaf.Disposed);
    }

    // ---- Singleton の accessor フィールドキャッシュ / singleton accessor field cache ----

    public sealed class CountingSingleton
    {
        private static int created;

        public static int Created => created;

        public CountingSingleton()
        {
            Interlocked.Increment(ref created);
        }
    }

    [Fact]
    public void SingletonFirstResolutionIsThreadSafe()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<CountingSingleton>()
            .BuildResolverServiceProvider();

        var results = new CountingSingleton[16];
        Parallel.For(0, results.Length, i => results[i] = provider.GetRequiredService<CountingSingleton>());

        Assert.All(results, x => Assert.Same(results[0], x));
        Assert.Equal(1, CountingSingleton.Created);
    }

    [Fact]
    public void InlinedGraphFallsBackWhenDependencyRegistrationIsReplaced()
    {
        // ServiceDescriptor 直接登録はジェネレータから見えない実行時差し替え。インライン展開の前提が
        // 崩れるため、生成ファクトリは採用されず差し替え後の型が解決されること
        // Direct ServiceDescriptor registration is a runtime replacement invisible to the generator. It breaks
        // the inline assumptions, so the generated factory must be rejected and the replaced type resolved.
        var services = new ServiceCollection().AddComponents();
        services.Add(ServiceDescriptor.Describe(typeof(LeafDependency), typeof(DerivedLeafDependency), ServiceLifetime.Transient));
        using var provider = services.BuildResolverServiceProvider();

        var root = provider.GetRequiredService<GraphRoot>();

        Assert.IsType<DerivedLeafDependency>(root.A.Leaf);
        Assert.IsType<DerivedLeafDependency>(root.B.Leaf);
        Assert.NotSame(root.A.Leaf, root.B.Leaf);
    }
}
