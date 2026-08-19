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
        // Arrange
        using var provider = CreateProvider();

        // Act & Assert
        Assert.Same(((IServiceProvider)provider).GetRequiredService(typeof(SingletonComponent)), provider.GetRequiredService<SingletonComponent>());
        Assert.Same(provider.GetRequiredService<SingletonComponent>(), provider.GetService<SingletonComponent>());
        Assert.Null(provider.GetService<ComponentResolutionTest>());

        // Arrange
        using var scope = provider.CreateScope();
        var scopeProvider = (ServiceProviderScope)scope.ServiceProvider;

        // Act & Assert
        Assert.Same(provider.GetRequiredService<SingletonComponent>(), scopeProvider.GetService<SingletonComponent>());
    }

    [Fact]
    public void KeyedFactoryReceivesResolvedDependencies()
    {
        // Arrange
        using var provider = CreateProvider();

        var first = provider.GetRequiredKeyedService<IKeyedWithDependency>("kd");

        // Act
        var second = provider.GetRequiredKeyedService<IKeyedWithDependency>("kd");

        // Assert
        Assert.NotSame(first, second);
        Assert.Equal("kd", first.Key);
        Assert.Same(provider.GetRequiredService<KeyedProbeDependency>(), first.Probe);
        Assert.Same(first.Probe, second.Probe);
    }

    [Fact]
    public void FactoryReportClassifiesResolutionPaths()
    {
        // Arrange
        var services = new ServiceCollection().AddGeneratedComponents();
        services.Add(ServiceDescriptor.Describe(typeof(UntrackedProbe), typeof(UntrackedProbe), ServiceLifetime.Transient));
        services.AddSingleton<IUntrackedProbe>(static _ => new UntrackedProbe());
        using var provider = services.BuildGeneratedServiceProvider();

        // Act
        var report = provider.CreateFactoryReport();

        // Assert
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
        // Arrange
        var services = new ServiceCollection().AddGeneratedComponents();
        services.Add(ServiceDescriptor.Describe(typeof(UntrackedProbe), typeof(UntrackedProbe), ServiceLifetime.Transient));
        using var provider = services.BuildGeneratedServiceProvider();

        // Act
        var text = provider.DescribeRuntimeFallbacks();

        // Assert
        Assert.Contains("[assembly: global::BunnyTail.DependencyInjection.GenerateComponentFactory(typeof(global::", text, StringComparison.Ordinal);
        Assert.Contains(typeof(UntrackedProbe).FullName!.Replace('+', '.'), text, StringComparison.Ordinal);
    }

    [Fact]
    public void FactoryReportDescribeAcceptsPredicate()
    {
        // Arrange
        var services = new ServiceCollection().AddGeneratedComponents();
        services.Add(ServiceDescriptor.Describe(typeof(UntrackedProbe), typeof(UntrackedProbe), ServiceLifetime.Transient));
        using var provider = services.BuildGeneratedServiceProvider();

        var all = provider.DescribeRuntimeFallbacks();

        // Act
        var filtered = provider.DescribeRuntimeFallbacks(static x => x.Lifetime != ServiceLifetime.Transient);

        // Assert
        Assert.Contains(typeof(UntrackedProbe).FullName!, all, StringComparison.Ordinal);
        Assert.DoesNotContain(typeof(UntrackedProbe).FullName!, filtered, StringComparison.Ordinal);
    }

    [Fact]
    public void FactoryReportDescribeAcceptsFormatter()
    {
        // Arrange
        var services = new ServiceCollection().AddGeneratedComponents();
        services.Add(ServiceDescriptor.Describe(typeof(UntrackedProbe), typeof(UntrackedProbe), ServiceLifetime.Transient));
        using var provider = services.BuildGeneratedServiceProvider();

        // Act
        var text = provider.DescribeRuntimeFallbacks(formatter: static (entry, typeName) => $"{entry.Lifetime}: {typeName}");

        // Assert
        Assert.Contains($"Transient: {typeof(UntrackedProbe).FullName!.Replace('+', '.')}", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[assembly:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SingletonIsSameAcrossScopes()
    {
        // Arrange
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var root = provider.GetRequiredService<SingletonComponent>();

        // Act
        var scoped = scope.ServiceProvider.GetRequiredService<SingletonComponent>();

        // Assert
        Assert.Same(root, scoped);
    }

    [Fact]
    public void TransientIsDistinct()
    {
        // Arrange
        using var provider = CreateProvider();

        var a = provider.GetRequiredService<TransientComponent>();

        // Act
        var b = provider.GetRequiredService<TransientComponent>();

        // Assert
        Assert.NotSame(a, b);
        Assert.Same(a.Singleton, b.Singleton);
    }

    [Fact]
    public void InjectPropertyIsInjected()
    {
        // Arrange
        using var provider = CreateProvider();

        var a = provider.GetRequiredService<TransientComponent>();

        // Act
        var b = provider.GetRequiredService<TransientComponent>();

        // Assert
        Assert.NotNull(a.Prop);
        Assert.Same(a.Prop, b.Prop);   // PropDependency is a singleton
        Assert.Same(a.Prop, provider.GetRequiredService<PropDependency>());
    }

    [Fact]
    public void ScopedIsPerScopeAndForwardedInterfaceIsSameInstance()
    {
        // Arrange
        using var provider = CreateProvider();
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var byClass = scope1.ServiceProvider.GetRequiredService<ScopedComponent>();
        var byInterface = scope1.ServiceProvider.GetRequiredService<IScopedService>();

        // Act
        var other = scope2.ServiceProvider.GetRequiredService<IScopedService>();

        // Assert
        Assert.Same(byClass, byInterface);
        Assert.NotSame(byInterface, other);
    }

    [Fact]
    public void KeyedComponentIsResolved()
    {
        // Arrange
        using var provider = CreateProvider();

        // Act
        var service = provider.GetRequiredKeyedService<IKeyedService>("primary");

        // Assert
        Assert.IsType<PrimaryKeyedComponent>(service);
        Assert.Null(provider.GetService<IKeyedService>());
    }

    [Fact]
    public void SingletonIsDisposedWithProvider()
    {
        // Arrange
        DisposableSingleton singleton;

        // Act
        using (var provider = CreateProvider())
        {
            singleton = provider.GetRequiredService<DisposableSingleton>();
            Assert.False(singleton.Disposed);
        }

        // Assert
        Assert.True(singleton.Disposed);
    }

    //--------------------------------------------------------------------------------
    // Transient inline expansion
    //--------------------------------------------------------------------------------

    [Fact]
    public void TransientGraphDependenciesAreFreshPerUseSite()
    {
        // Arrange
        using var provider = CreateProvider();

        var root = provider.GetRequiredService<GraphRoot>();

        // MEDI compatible
        Assert.NotSame(root.A.Leaf, root.B.Leaf);

        // Act
        var other = provider.GetRequiredService<GraphRoot>();

        // Assert
        Assert.NotSame(root, other);
        Assert.NotSame(root.A, other.A);
        Assert.NotSame(root.A.Leaf, other.A.Leaf);
    }

    [Fact]
    public void DisposableTransientDependencyIsTrackedByScope()
    {
        // Arrange
        using var provider = CreateProvider();

        DisposableLeaf leaf;

        // Act
        using (var scope = provider.CreateScope())
        {
            leaf = scope.ServiceProvider.GetRequiredService<NodeWithDisposable>().Leaf;
            Assert.False(leaf.Disposed);
        }

        // Assert
        Assert.True(leaf.Disposed);
    }

    //--------------------------------------------------------------------------------
    // Singleton dependency
    //--------------------------------------------------------------------------------

    [Fact]
    public void DepsShapedFactoryFallsBackWhenSingletonLifetimeIsReplaced()
    {
        // Arrange
        var services = new ServiceCollection().AddGeneratedComponents();
        services.Add(ServiceDescriptor.Describe(typeof(SingletonComponent), typeof(SingletonComponent), ServiceLifetime.Transient));
        using var provider = services.BuildGeneratedServiceProvider();

        var a = provider.GetRequiredService<TransientComponent>();

        // Act
        var b = provider.GetRequiredService<TransientComponent>();

        // Assert
        Assert.NotSame(a.Singleton, b.Singleton);
    }

    [Fact]
    public void DepsFillingIsLazy()
    {
        // Arrange
        var before = LazyProbeSingleton.Created;

        // Act & Assert
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
        // Arrange
        using var provider = CreateProvider();

        var first = provider.GetServices<IMultiLeaf>().ToArray();

        // Act
        var second = provider.GetServices<IMultiLeaf>().ToArray();

        // Assert
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
        // Arrange
        var services = new ServiceCollection().AddGeneratedComponents();
        services.Add(ServiceDescriptor.Describe(typeof(IMultiLeaf), typeof(RuntimeMultiLeaf), ServiceLifetime.Transient));
        using var provider = services.BuildGeneratedServiceProvider();

        // Act
        var all = provider.GetServices<IMultiLeaf>().ToArray();

        // Assert
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
        // Arrange
        IServiceCollection services = new ServiceCollection();
        services.AddTransient(typeof(IGenericContainer<>), typeof(GenericContainer<>));
        using var provider = services.BuildGeneratedServiceProvider();

        // Act & Assert
        var closedType = typeof(IGenericContainer<string>);
        Assert.IsType<GenericContainer<string>>(provider.GetService(closedType));

        var runtimeClosed = typeof(IGenericContainer<>).MakeGenericType(typeof(Guid));
        Assert.IsType<GenericContainer<Guid>>(provider.GetService(runtimeClosed));
    }

    [Fact]
    public void ValueTypeEnumerableUsesFallbackPath()
    {
        // Arrange
        IServiceCollection services = new ServiceCollection();
        services.Add(ServiceDescriptor.Singleton(typeof(int), 1));
        services.Add(ServiceDescriptor.Singleton(typeof(int), 2));

        // Act
        using var provider = services.BuildGeneratedServiceProvider();

        // Assert
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
        // Arrange
        using var provider = CreateProvider();

        // Act & Assert
        Assert.True(provider.GetRequiredService<PostConstructComponent>().Initialized);
    }

    [Fact]
    public void InitializableInterfaceIsInvoked()
    {
        // Arrange
        using var provider = CreateProvider();

        // Act & Assert
        Assert.True(provider.GetRequiredService<InitializableComponent>().Initialized);
    }

    [Fact]
    public void InitializationRunsAfterPropertyInjection()
    {
        // Arrange
        using var provider = CreateProvider();

        // Act & Assert
        Assert.True(provider.GetRequiredService<OrderedInitComponent>().PropWasSetOnInitialize);
    }

    [Fact]
    public void PostConstructIsInvokedOnReflectionPath()
    {
        // Arrange
        using var provider = CreateProvider();

        // Act & Assert
        Assert.True(provider.GetRequiredService<ReflectionInitComponent>().Initialized);
    }

    [Fact]
    public void RuntimeRegisteredInitializableIsInvoked()
    {
        // Arrange
        IServiceCollection services = new ServiceCollection();
        services.Add(ServiceDescriptor.Describe(typeof(RuntimeInitializable), typeof(RuntimeInitializable), ServiceLifetime.Transient));

        // Act
        using var provider = services.BuildGeneratedServiceProvider();

        // Assert
        Assert.True(provider.GetRequiredService<RuntimeInitializable>().Initialized);
    }

    [Fact]
    public void FactoryRegistrationIsNotInitialized()
    {
        // Arrange
        using var provider = new ServiceCollection()
            .AddTransient(static _ => new RuntimeInitializable())
            .BuildGeneratedServiceProvider();

        // Act & Assert
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
        // Arrange
        using var provider = new ServiceCollection()
            .AddSingleton<CountingSingleton>()
            .BuildGeneratedServiceProvider();

        var results = new CountingSingleton[16];

        // Act
        // ReSharper disable once AccessToDisposedClosure
        Parallel.For(0, results.Length, i => results[i] = provider.GetRequiredService<CountingSingleton>());

        // Assert
        Assert.All(results, x => Assert.Same(results[0], x));
        Assert.Equal(1, CountingSingleton.Created);
    }

    [Fact]
    public void InlinedGraphFallsBackWhenDependencyRegistrationIsReplaced()
    {
        // Arrange
        var services = new ServiceCollection().AddGeneratedComponents();
        services.Add(ServiceDescriptor.Describe(typeof(LeafDependency), typeof(DerivedLeafDependency), ServiceLifetime.Transient));
        using var provider = services.BuildGeneratedServiceProvider();

        // Act
        var root = provider.GetRequiredService<GraphRoot>();

        // Assert
        Assert.IsType<DerivedLeafDependency>(root.A.Leaf);
        Assert.IsType<DerivedLeafDependency>(root.B.Leaf);
        Assert.NotSame(root.A.Leaf, root.B.Leaf);
    }
}
