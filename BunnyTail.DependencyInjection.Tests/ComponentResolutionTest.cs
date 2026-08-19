namespace BunnyTail.DependencyInjection.Tests;

using BunnyTail.DependencyInjection;
using BunnyTail.DependencyInjection.Diagnostics;
using BunnyTail.DependencyInjection.Tests.Components;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

public sealed class ComponentResolutionTest
{
    private static GeneratedServiceProvider CreateProvider() =>
        new ServiceCollection().AddGeneratedComponents().BuildGeneratedServiceProvider();

    [Fact]
    public void TypedResolutionMatchesTypeBasedResolution()
    {
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
        var services = new ServiceCollection().AddGeneratedComponents();
        services.Add(ServiceDescriptor.Describe(typeof(UntrackedProbe), typeof(UntrackedProbe), ServiceLifetime.Transient));
        services.AddSingleton<IUntrackedProbe>(static _ => new UntrackedProbe());
        using var provider = services.BuildGeneratedServiceProvider();

        var report = provider.CreateFactoryReport();

        Assert.Equal(
            ServiceFactoryStatus.Generated,
            report.First(static x => x.ImplementationType == typeof(SingletonComponent)).Status);

        Assert.Equal(
            ServiceFactoryStatus.RuntimeFallback,
            report.First(static x => x.ServiceType == typeof(UntrackedProbe)).Status);

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

        Assert.Contains("[assembly: global::BunnyTail.DependencyInjection.GenerateComponentFactory(typeof(global::", text, StringComparison.Ordinal);
        Assert.Contains(typeof(UntrackedProbe).FullName!.Replace('+', '.'), text, StringComparison.Ordinal);
    }

    [Fact]
    public void FactoryReportDescribeAcceptsPredicate()
    {
        var services = new ServiceCollection().AddGeneratedComponents();
        services.Add(ServiceDescriptor.Describe(typeof(UntrackedProbe), typeof(UntrackedProbe), ServiceLifetime.Transient));
        using var provider = services.BuildGeneratedServiceProvider();

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

        var text = provider.DescribeRuntimeFallbacks(formatter: static (entry, typeName) => $"{entry.Lifetime}: {typeName}");

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

        Assert.Same(byClass, byInterface);
        Assert.NotSame(byInterface, other);
    }

    [Fact]
    public void KeyedComponentIsResolved()
    {
        using var provider = CreateProvider();

        var service = provider.GetRequiredKeyedService<IKeyedService>("primary");

        Assert.IsType<PrimaryKeyedComponent>(service);
        Assert.Null(provider.GetService<IKeyedService>());
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

    //--------------------------------------------------------------------------------
    // Transient inline expansion
    //--------------------------------------------------------------------------------

    [Fact]
    public void TransientGraphDependenciesAreFreshPerUseSite()
    {
        using var provider = CreateProvider();

        var root = provider.GetRequiredService<GraphRoot>();

        // MEDI compatible
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

    //--------------------------------------------------------------------------------
    // Singleton dependency
    //--------------------------------------------------------------------------------

    [Fact]
    public void DepsShapedFactoryFallsBackWhenSingletonLifetimeIsReplaced()
    {
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
        //--------------------------------------------------------------------------------
        var before = LazyProbeSingleton.Created;
        using var provider = CreateProvider();
        Assert.Equal(before, LazyProbeSingleton.Created);

        var consumer = provider.GetRequiredService<LazyProbeConsumer>();
        Assert.Equal(before + 1, LazyProbeSingleton.Created);
        Assert.Same(consumer.Dependency, provider.GetRequiredService<LazyProbeSingleton>());
    }

    //--------------------------------------------------------------------------------
    // Enumerable
    //--------------------------------------------------------------------------------

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
        var services = new ServiceCollection().AddGeneratedComponents();
        services.Add(ServiceDescriptor.Describe(typeof(IMultiLeaf), typeof(RuntimeMultiLeaf), ServiceLifetime.Transient));
        using var provider = services.BuildGeneratedServiceProvider();

        var all = provider.GetServices<IMultiLeaf>().ToArray();

        Assert.Equal(3, all.Length);
        Assert.IsType<RuntimeMultiLeaf>(all[2]);
    }

    //--------------------------------------------------------------------------------
    // Open generic closed usage
    //--------------------------------------------------------------------------------

    // ReSharper disable once UnusedTypeParameter
    public interface IGenericContainer<T>;

    public sealed class GenericContainer<T> : IGenericContainer<T>;

    [Fact]
    public void OpenGenericClosedUsageResolvesThroughGeneratedFactory()
    {
        IServiceCollection services = new ServiceCollection();
        services.AddTransient(typeof(IGenericContainer<>), typeof(GenericContainer<>));
        using var provider = services.BuildGeneratedServiceProvider();

        var closedType = typeof(IGenericContainer<string>);
        Assert.IsType<GenericContainer<string>>(provider.GetService(closedType));

        var runtimeClosed = typeof(IGenericContainer<>).MakeGenericType(typeof(Guid));
        Assert.IsType<GenericContainer<Guid>>(provider.GetService(runtimeClosed));
    }

    [Fact]
    public void ValueTypeEnumerableUsesFallbackPath()
    {
        IServiceCollection services = new ServiceCollection();
        services.Add(ServiceDescriptor.Singleton(typeof(int), 1));
        services.Add(ServiceDescriptor.Singleton(typeof(int), 2));
        using var provider = services.BuildGeneratedServiceProvider();

        Assert.Equal([1, 2], provider.GetServices<int>());
    }

    //--------------------------------------------------------------------------------
    // Initialization
    //--------------------------------------------------------------------------------

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
        using var provider = CreateProvider();

        Assert.True(provider.GetRequiredService<ReflectionInitComponent>().Initialized);
    }

    [Fact]
    public void RuntimeRegisteredInitializableIsInvoked()
    {
        IServiceCollection services = new ServiceCollection();
        services.Add(ServiceDescriptor.Describe(typeof(RuntimeInitializable), typeof(RuntimeInitializable), ServiceLifetime.Transient));
        using var provider = services.BuildGeneratedServiceProvider();

        Assert.True(provider.GetRequiredService<RuntimeInitializable>().Initialized);
    }

    [Fact]
    public void FactoryRegistrationIsNotInitialized()
    {
        using var provider = new ServiceCollection()
            .AddTransient(static _ => new RuntimeInitializable())
            .BuildGeneratedServiceProvider();

        Assert.False(provider.GetRequiredService<RuntimeInitializable>().Initialized);
    }

    //--------------------------------------------------------------------------------
    // Singleton cache
    //--------------------------------------------------------------------------------

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
        // ReSharper disable once AccessToDisposedClosure
        Parallel.For(0, results.Length, i => results[i] = provider.GetRequiredService<CountingSingleton>());

        Assert.All(results, x => Assert.Same(results[0], x));
        Assert.Equal(1, CountingSingleton.Created);
    }

    [Fact]
    public void InlinedGraphFallsBackWhenDependencyRegistrationIsReplaced()
    {
        var services = new ServiceCollection().AddGeneratedComponents();
        services.Add(ServiceDescriptor.Describe(typeof(LeafDependency), typeof(DerivedLeafDependency), ServiceLifetime.Transient));
        using var provider = services.BuildGeneratedServiceProvider();

        var root = provider.GetRequiredService<GraphRoot>();

        Assert.IsType<DerivedLeafDependency>(root.A.Leaf);
        Assert.IsType<DerivedLeafDependency>(root.B.Leaf);
        Assert.NotSame(root.A.Leaf, root.B.Leaf);
    }
}
