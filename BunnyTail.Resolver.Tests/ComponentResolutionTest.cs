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

    // ---- singleton 依存の deps 配列渡し / singleton dependencies through the deps array ----

    [Fact]
    public void DepsShapedFactoryFallsBackWhenSingletonLifetimeIsReplaced()
    {
        // SingletonComponent を transient に差し替えると deps 前提 (singleton 解決) が崩れ、
        // 互換経路へフォールバックして transient セマンティクスが守られること
        // Replacing SingletonComponent as transient breaks the deps assumption (singleton resolution);
        // the factory must fall back to the runtime path and honor transient semantics.
        var services = new ServiceCollection().AddComponents();
        services.Add(ServiceDescriptor.Describe(typeof(SingletonComponent), typeof(SingletonComponent), ServiceLifetime.Transient));
        using var provider = services.BuildResolverServiceProvider();

        var a = provider.GetRequiredService<TransientComponent>();
        var b = provider.GetRequiredService<TransientComponent>();

        Assert.NotSame(a.Singleton, b.Singleton);
    }

    [Fact]
    public void DepsFillingIsLazy()
    {
        // deps 充填は消費側の初回解決時。プロバイダ構築だけでは singleton を生成しない (MEDI の lazy と一致)
        // Deps are filled on the consumer's first resolution; building the provider alone creates no singleton (matches MEDI laziness).
        var before = LazyProbeSingleton.Created;
        using var provider = CreateProvider();
        Assert.Equal(before, LazyProbeSingleton.Created);

        var consumer = provider.GetRequiredService<LazyProbeConsumer>();
        Assert.Equal(before + 1, LazyProbeSingleton.Created);
        Assert.Same(consumer.Dependency, provider.GetRequiredService<LazyProbeSingleton>());
    }

    // ---- open generic の閉型生成 / closed factories from open generic registrations ----

    public interface IGenericContainer<T>;

    public sealed class GenericContainer<T> : IGenericContainer<T>;

    [Fact]
    public void OpenGenericClosedUsageResolvesThroughGeneratedFactory()
    {
        // このテスト内の typeof(IGenericContainer<string>) が閉型使用として収集され、生成ファクトリが出力される
        // The typeof(IGenericContainer<string>) in this test doubles as the collected closed usage that produces a generated factory.
        IServiceCollection services = new ServiceCollection();
        services.AddTransient(typeof(IGenericContainer<>), typeof(GenericContainer<>));
        using var provider = services.BuildResolverServiceProvider();

        Assert.IsType<GenericContainer<string>>(provider.GetService(typeof(IGenericContainer<string>)));

        // コンパイル時に見えない閉型は従来どおり互換経路で解決される
        // Closed forms invisible at compile time keep resolving through the runtime path.
        var runtimeClosed = typeof(IGenericContainer<>).MakeGenericType(typeof(Guid));
        Assert.IsType<GenericContainer<Guid>>(provider.GetService(runtimeClosed));
    }

    [Fact]
    public void ValueTypeEnumerableUsesFallbackPath()
    {
        // 値型要素は型付き配列ファクトリを使えないため Array.CreateInstance 経路で実体化される
        // Value type elements cannot use the typed array factory and materialize through Array.CreateInstance.
        IServiceCollection services = new ServiceCollection();
        services.Add(ServiceDescriptor.Singleton(typeof(int), 1));
        services.Add(ServiceDescriptor.Singleton(typeof(int), 2));
        using var provider = services.BuildResolverServiceProvider();

        Assert.Equal([1, 2], provider.GetServices<int>());
    }

    // ---- 初期化コールバック / initialization callbacks ----

    public sealed class RuntimeInitializable : IInitializable
    {
        public bool Initialized { get; private set; }

        public void Initialize() => Initialized = true;
    }

    [Fact]
    public void PostConstructMethodIsInvoked()
    {
        using var provider = CreateProvider();

        Assert.True(provider.GetRequiredService<PostConstructComponent>().Initialized);
    }

    [Fact]
    public void InitializableInterfaceIsInvoked()
    {
        using var provider = CreateProvider();

        Assert.True(provider.GetRequiredService<InitializableComponent>().Initialized);
    }

    [Fact]
    public void InitializationRunsAfterPropertyInjection()
    {
        using var provider = CreateProvider();

        Assert.True(provider.GetRequiredService<OrderedInitComponent>().PropWasSetOnInitialize);
    }

    [Fact]
    public void PostConstructIsInvokedOnReflectionPath()
    {
        // ReflectionInitComponent は既定値付き引数のため生成ファクトリ不適格 → 互換経路で解決される
        // ReflectionInitComponent has a default-valued parameter, so it resolves through the runtime path.
        using var provider = CreateProvider();

        Assert.True(provider.GetRequiredService<ReflectionInitComponent>().Initialized);
    }

    [Fact]
    public void RuntimeRegisteredInitializableIsInvoked()
    {
        // ServiceDescriptor 直接登録はジェネレータから見えない → 互換経路の IInitializable 呼び出しを検証
        // Direct ServiceDescriptor registration is invisible to the generator, exercising IInitializable on the runtime path.
        IServiceCollection services = new ServiceCollection();
        services.Add(ServiceDescriptor.Describe(typeof(RuntimeInitializable), typeof(RuntimeInitializable), ServiceLifetime.Transient));
        using var provider = services.BuildResolverServiceProvider();

        Assert.True(provider.GetRequiredService<RuntimeInitializable>().Initialized);
    }

    [Fact]
    public void FactoryRegistrationIsNotInitialized()
    {
        // ファクトリ登録はユーザー所有の生成なので初期化しない
        // Factory registrations are user-owned construction and are never initialized.
        using var provider = new ServiceCollection()
            .AddTransient(static _ => new RuntimeInitializable())
            .BuildResolverServiceProvider();

        Assert.False(provider.GetRequiredService<RuntimeInitializable>().Initialized);
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
