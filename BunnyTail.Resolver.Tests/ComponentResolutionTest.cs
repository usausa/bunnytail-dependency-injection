namespace BunnyTail.Resolver.Tests;

using BunnyTail.Resolver;
using BunnyTail.Resolver.Diagnostics;
using BunnyTail.Resolver.Tests.Components;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

// 属性コンポーネント (生成された AddGeneratedComponents + 生成ファクトリ) の機能検証
// Functional verification of attribute components (generated AddGeneratedComponents + generated factories).
public sealed class ComponentResolutionTest
{
    private static ResolverServiceProvider CreateProvider() =>
        new ServiceCollection().AddGeneratedComponents().BuildGeneratedServiceProvider();

    [Fact]
    public void TypedResolutionMatchesTypeBasedResolution()
    {
        // S-5: provider / scope の型付きインスタンスメソッドが Type ベース解決と同一の結果を返す
        // S-5: the typed instance methods on the provider and scope return the same results as Type based resolution.
        using var provider = CreateProvider();

        Assert.Same(((IServiceProvider)provider).GetRequiredService(typeof(SingletonComponent)), provider.GetRequiredService<SingletonComponent>());
        Assert.Same(provider.GetRequiredService<SingletonComponent>(), provider.GetService<SingletonComponent>());
        Assert.Null(provider.GetService<ComponentResolutionTest>());

        using var scope = provider.CreateScope();
        var scopeProvider = (ServiceProviderScope)scope.ServiceProvider;
        Assert.Same(provider.GetRequiredService<SingletonComponent>(), scopeProvider.GetService<SingletonComponent>());
    }

    [Fact]
    public void KeyedFactoryReceivesResolvedDependencies()
    {
        // keyed deps 形: singleton 依存は deps スロット経由、[ServiceKey] は key 引数経由で注入される
        // Keyed deps shape: the singleton dependency arrives through a deps slot and [ServiceKey] through the key argument.
        using var provider = CreateProvider();

        var first = provider.GetRequiredKeyedService<IKeyedWithDependency>("kd");
        var second = provider.GetRequiredKeyedService<IKeyedWithDependency>("kd");

        Assert.NotSame(first, second);
        Assert.Equal("kd", first.Key);
        Assert.Same(provider.GetRequiredService<KeyedProbeDependency>(), first.Probe);
        Assert.Same(first.Probe, second.Probe);
    }

    [Fact]
    public void FactoryReportClassifiesResolutionPaths()
    {
        // 開発時診断: 生成ファクトリ採用・実行時経路・生成対象外を分類する
        // Development-time diagnostics classify generated adoption, the runtime path and non-applicable registrations.
        var services = new ServiceCollection().AddGeneratedComponents();
        services.Add(ServiceDescriptor.Describe(typeof(UntrackedProbe), typeof(UntrackedProbe), ServiceLifetime.Transient));
        services.AddSingleton<IUntrackedProbe>(static _ => new UntrackedProbe());
        using var provider = services.BuildGeneratedServiceProvider();

        var report = provider.CreateFactoryReport();

        // 属性コンポーネント = 生成経路 / attribute component resolves through the generated path
        Assert.Equal(
            ServiceFactoryStatus.Generated,
            report.First(static x => x.ImplementationType == typeof(SingletonComponent)).Status);

        // 生成器から見えない登録 = 実行時経路 ([GenerateComponentFactory] の候補)
        // A registration invisible to the generator takes the runtime path (a [GenerateComponentFactory] candidate).
        Assert.Equal(
            ServiceFactoryStatus.RuntimeFallback,
            report.First(static x => x.ServiceType == typeof(UntrackedProbe)).Status);

        // ファクトリ登録は構築自体がユーザーのデリゲート = 生成対象外
        // A factory registration constructs through the user's delegate, so nothing can be generated.
        Assert.Equal(
            ServiceFactoryStatus.NotApplicable,
            report.First(static x => x.ServiceType == typeof(IUntrackedProbe)).Status);
    }

    [Fact]
    public void FactoryReportDescribesRuntimeFallbacks()
    {
        var services = new ServiceCollection().AddGeneratedComponents();
        services.Add(ServiceDescriptor.Describe(typeof(UntrackedProbe), typeof(UntrackedProbe), ServiceLifetime.Transient));
        using var provider = services.BuildGeneratedServiceProvider();

        var text = provider.DescribeRuntimeFallbacks();

        // そのまま貼り付けられる属性行として出力される / emitted as ready-to-paste attribute lines
        Assert.Contains("[assembly: global::BunnyTail.Resolver.GenerateComponentFactory(typeof(global::", text, StringComparison.Ordinal);
        Assert.Contains(typeof(UntrackedProbe).FullName!.Replace('+', '.'), text, StringComparison.Ordinal);
    }

    [Fact]
    public void FactoryReportDescribeAcceptsPredicate()
    {
        var services = new ServiceCollection().AddGeneratedComponents();
        services.Add(ServiceDescriptor.Describe(typeof(UntrackedProbe), typeof(UntrackedProbe), ServiceLifetime.Transient));
        using var provider = services.BuildGeneratedServiceProvider();

        // 述語で絞り込める (ここでは transient を除外するので候補が消える)
        // A predicate narrows the set; excluding transients here leaves no candidate.
        var all = provider.DescribeRuntimeFallbacks();
        var filtered = provider.DescribeRuntimeFallbacks(static x => x.Lifetime != ServiceLifetime.Transient);

        Assert.Contains(typeof(UntrackedProbe).FullName!, all, StringComparison.Ordinal);
        Assert.DoesNotContain(typeof(UntrackedProbe).FullName!, filtered, StringComparison.Ordinal);
    }

    [Fact]
    public void FactoryReportDescribeAcceptsFormatter()
    {
        var services = new ServiceCollection().AddGeneratedComponents();
        services.Add(ServiceDescriptor.Describe(typeof(UntrackedProbe), typeof(UntrackedProbe), ServiceLifetime.Transient));
        using var provider = services.BuildGeneratedServiceProvider();

        // 書式を差し替えられる。型名は C# として正しい形で渡される
        // The format can be replaced; the type name arrives already valid in C#.
        var text = provider.DescribeRuntimeFallbacks(
            formatter: static (entry, typeName) => $"{entry.Lifetime}: {typeName}");

        Assert.Contains($"Transient: {typeof(UntrackedProbe).FullName!.Replace('+', '.')}", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[assembly:", text, StringComparison.Ordinal);
    }

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
        var services = new ServiceCollection().AddGeneratedComponents();
        services.Add(ServiceDescriptor.Describe(typeof(SingletonComponent), typeof(SingletonComponent), ServiceLifetime.Transient));
        using var provider = services.BuildGeneratedServiceProvider();

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

    // ---- 生成 enumerable ファクトリ / generated enumerable factories ----

    public sealed class RuntimeMultiLeaf : IMultiLeaf;

    [Fact]
    public void TransientEnumerableMaterializesInRegistrationOrder()
    {
        using var provider = CreateProvider();

        var first = provider.GetServices<IMultiLeaf>().ToArray();
        var second = provider.GetServices<IMultiLeaf>().ToArray();

        Assert.Collection(
            first,
            static x => Assert.IsType<MultiLeafA>(x),
            static x => Assert.IsType<MultiLeafB>(x));
        Assert.NotSame(first[0], second[0]);
        Assert.NotSame(first[1], second[1]);
    }

    [Fact]
    public void GeneratedEnumerableFallsBackWhenElementIsAdded()
    {
        // 実行時に要素を追加すると数の前提が崩れ、accessor 経由の実体化へフォールバックする
        // Adding an element at runtime breaks the count assumption and falls back to accessor-based materialization.
        var services = new ServiceCollection().AddGeneratedComponents();
        services.Add(ServiceDescriptor.Describe(typeof(IMultiLeaf), typeof(RuntimeMultiLeaf), ServiceLifetime.Transient));
        using var provider = services.BuildGeneratedServiceProvider();

        var all = provider.GetServices<IMultiLeaf>().ToArray();

        Assert.Equal(3, all.Length);
        Assert.IsType<RuntimeMultiLeaf>(all[2]);
    }

    // ---- open generic の閉型生成 / closed factories from open generic registrations ----

    // open generic 登録の検証用マーカー。型引数は登録形状のためだけに必要
    // Marker for verifying open generic registration; the type parameter exists only to shape the registration.
    // ReSharper disable once UnusedTypeParameter
    public interface IGenericContainer<T>;

    public sealed class GenericContainer<T> : IGenericContainer<T>;

    [Fact]
    public void OpenGenericClosedUsageResolvesThroughGeneratedFactory()
    {
        // このテスト内の typeof(IGenericContainer<string>) が閉型使用として収集され、生成ファクトリが出力される
        // The typeof(IGenericContainer<string>) in this test doubles as the collected closed usage that produces a generated factory.
        IServiceCollection services = new ServiceCollection();
        services.AddTransient(typeof(IGenericContainer<>), typeof(GenericContainer<>));
        using var provider = services.BuildGeneratedServiceProvider();

        var closedType = typeof(IGenericContainer<string>);
        Assert.IsType<GenericContainer<string>>(provider.GetService(closedType));

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
        using var provider = services.BuildGeneratedServiceProvider();

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
        using var provider = services.BuildGeneratedServiceProvider();

        Assert.True(provider.GetRequiredService<RuntimeInitializable>().Initialized);
    }

    [Fact]
    public void FactoryRegistrationIsNotInitialized()
    {
        // ファクトリ登録はユーザー所有の生成なので初期化しない
        // Factory registrations are user-owned construction and are never initialized.
        using var provider = new ServiceCollection()
            .AddTransient(static _ => new RuntimeInitializable())
            .BuildGeneratedServiceProvider();

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
            .BuildGeneratedServiceProvider();

        var results = new CountingSingleton[16];
        // Parallel.For は using スコープを抜ける前に完走するため、捕捉した provider は破棄されていない
        // Parallel.For completes before the using scope ends, so the captured provider is not disposed yet.
        // ReSharper disable once AccessToDisposedClosure
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
        var services = new ServiceCollection().AddGeneratedComponents();
        services.Add(ServiceDescriptor.Describe(typeof(LeafDependency), typeof(DerivedLeafDependency), ServiceLifetime.Transient));
        using var provider = services.BuildGeneratedServiceProvider();

        var root = provider.GetRequiredService<GraphRoot>();

        Assert.IsType<DerivedLeafDependency>(root.A.Leaf);
        Assert.IsType<DerivedLeafDependency>(root.B.Leaf);
        Assert.NotSame(root.A.Leaf, root.B.Leaf);
    }
}
