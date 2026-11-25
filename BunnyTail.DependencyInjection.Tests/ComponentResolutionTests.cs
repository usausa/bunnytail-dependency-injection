namespace BunnyTail.DependencyInjection.Tests;

using BunnyTail.DependencyInjection;
using BunnyTail.DependencyInjection.Diagnostics;
using BunnyTail.DependencyInjection.Internal;
using BunnyTail.DependencyInjection.Tests.Components;

using Microsoft.Extensions.DependencyInjection;

public sealed class ComponentResolutionTests
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
        Assert.Null(provider.GetService<ComponentResolutionTests>());

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
    public void KeyedDependenciesFillConstructorParameterAndInjectProperty()
    {
        // Arrange
        using var provider = CreateProvider();

        // Act
        var consumer = provider.GetRequiredService<KeyedConsumer>();

        // Assert
        Assert.Equal("left", consumer.Left.Name);
        Assert.Equal("right", consumer.Right.Name);
        Assert.Same(provider.GetRequiredKeyedService<IKeyedLeaf>("left"), consumer.Left);
        Assert.Same(provider.GetRequiredKeyedService<IKeyedLeaf>("right"), consumer.Right);
    }

    [Fact]
    public void InjectKeyIsIndependentOfTheResolvingKey()
    {
        // Arrange
        using var provider = CreateProvider();

        // Act
        var consumer = provider.GetRequiredKeyedService<IKeyedPropertyConsumer>("left");

        // Assert
        Assert.Same(provider.GetRequiredKeyedService<IKeyedLeaf>("right"), consumer.Leaf);
    }

    [Fact]
    public void KeyedInjectPropertyStaysOnTheGeneratedPath()
    {
        // Arrange: a literal key needs no resolving key, so a non-keyed registration keeps its generated factory
        using var provider = CreateProvider();

        // Act
        var entry = provider.CreateFactoryReport().First(static x => x.ImplementationType == typeof(KeyedConsumer));

        // Assert
        Assert.Equal(ServiceFactoryStatus.Generated, entry.Status);
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
    public void FactoryReportSkipsTypeWithDefaultedConstructorParameter()
    {
        // Arrange: the generator refuses a constructor with a defaulted parameter, so it must not be suggested
        var services = new ServiceCollection().AddGeneratedComponents();
        services.Add(ServiceDescriptor.Describe(typeof(DefaultedParameterProbe), typeof(DefaultedParameterProbe), ServiceLifetime.Transient));
        using var provider = services.BuildGeneratedServiceProvider();

        // Act
        var report = provider.CreateFactoryReport();
        var text = provider.DescribeRuntimeFallbacks();

        // Assert
        var entry = report.First(static x => x.ImplementationType == typeof(DefaultedParameterProbe));
        Assert.Equal(ServiceFactoryStatus.RuntimeFallback, entry.Status);
        Assert.False(entry.CanGenerateFactory);
        Assert.DoesNotContain(nameof(DefaultedParameterProbe), text, StringComparison.Ordinal);
    }

    public sealed class DefaultedParameterProbe
    {
        public DefaultedParameterProbe(int value = 0)
        {
            Value = value;
        }

        public int Value { get; }
    }

    [Theory]
    [InlineData(typeof(AmbiguousConstructorProbe), null)]
    [InlineData(typeof(ObsoleteConstructorProbe), null)]
    [InlineData(typeof(RequiredMemberProbe), null)]
    [InlineData(typeof(ServiceKeyProbe), "probe")]
    [InlineData(typeof(InheritedKeyProbe), null)]
    [InlineData(typeof(GenericArgumentProbe<HiddenArgumentProbe>), null)]
    [InlineData(typeof(UntrackedProbe), "probe")]
    [InlineData(typeof(ConstructorMismatchProbe), null)]
    public void FactoryReportSkipsRegistrationWithoutUsableGeneratedFactory(Type type, string? key)
    {
        // Arrange
        var services = new ServiceCollection().AddGeneratedComponents();
        services.Add(key is null
            ? ServiceDescriptor.Describe(type, type, ServiceLifetime.Transient)
            : ServiceDescriptor.DescribeKeyed(type, key, type, ServiceLifetime.Transient));
        using var provider = services.BuildGeneratedServiceProvider();

        // Act
        var report = provider.CreateFactoryReport();
        var text = provider.DescribeRuntimeFallbacks(x => (x.ImplementationType == type) && Equals(x.ServiceKey, key));

        // Assert
        var entry = report.First(x => (x.ImplementationType == type) && Equals(x.ServiceKey, key));
        Assert.Equal(ServiceFactoryStatus.RuntimeFallback, entry.Status);
        Assert.False(entry.CanGenerateFactory);
        Assert.Empty(text);
    }

    public sealed class AmbiguousConstructorProbe
    {
        public AmbiguousConstructorProbe(SingletonComponent component)
        {
            Component = component;
        }

        public AmbiguousConstructorProbe(UntrackedProbe probe)
        {
            Probe = probe;
        }

        public SingletonComponent? Component { get; }

        public UntrackedProbe? Probe { get; }
    }

    public sealed class ObsoleteConstructorProbe
    {
        [Obsolete("Probe", true)]
        public ObsoleteConstructorProbe()
        {
        }
    }

    public sealed class RequiredMemberProbe
    {
        public required string Name { get; set; }
    }

    public sealed class ServiceKeyProbe
    {
        public ServiceKeyProbe([ServiceKey] string key)
        {
            Key = key;
        }

        public string Key { get; }
    }

    public sealed class InheritedKeyProbe
    {
        public InheritedKeyProbe([FromKeyedServices] SingletonComponent component)
        {
            Component = component;
        }

        public SingletonComponent Component { get; }
    }

    public sealed class ConstructorMismatchProbe
    {
        public ConstructorMismatchProbe(SingletonComponent component, UntrackedProbe probe)
        {
            Component = component;
            Probe = probe;
        }

        public ConstructorMismatchProbe(SingletonComponent component)
        {
            Component = component;
        }

        public SingletonComponent Component { get; }

        public UntrackedProbe? Probe { get; }
    }

    [Fact]
    public void FactoryReportDescribesNestedAndArrayTypes()
    {
        // Arrange
        var services = new ServiceCollection().AddGeneratedComponents();
        services.Add(ServiceDescriptor.Describe(typeof(GenericOuterProbe<int>.Inner), typeof(GenericOuterProbe<int>.Inner), ServiceLifetime.Transient));
        services.Add(ServiceDescriptor.Describe(typeof(GenericArgumentProbe<List<int>[]>), typeof(GenericArgumentProbe<List<int>[]>), ServiceLifetime.Transient));
        services.Add(ServiceDescriptor.Describe(typeof(GenericArgumentProbe<int[][,]>), typeof(GenericArgumentProbe<int[][,]>), ServiceLifetime.Transient));
        services.Add(ServiceDescriptor.Describe(typeof(GenericArgumentProbe<DefaultedParameterProbe[]>), typeof(GenericArgumentProbe<DefaultedParameterProbe[]>), ServiceLifetime.Transient));
        using var provider = services.BuildGeneratedServiceProvider();

        // Act
        var lines = provider.DescribeRuntimeFallbacks(formatter: static (_, typeName) => typeName).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        // Assert
        Assert.Contains("BunnyTail.DependencyInjection.Tests.ComponentResolutionTests.GenericOuterProbe<global::System.Int32>.Inner", lines);
        Assert.Contains("BunnyTail.DependencyInjection.Tests.ComponentResolutionTests.GenericArgumentProbe<global::System.Collections.Generic.List<global::System.Int32>[]>", lines);
        Assert.Contains("BunnyTail.DependencyInjection.Tests.ComponentResolutionTests.GenericArgumentProbe<global::System.Int32[][,]>", lines);
        Assert.Contains("BunnyTail.DependencyInjection.Tests.ComponentResolutionTests.GenericArgumentProbe<global::BunnyTail.DependencyInjection.Tests.ComponentResolutionTests.DefaultedParameterProbe[]>", lines);
    }

    public sealed class GenericOuterProbe<T>
    {
        public T? Value { get; set; }

        public sealed class Inner;
    }

    public sealed class GenericArgumentProbe<T>
    {
        public T? Value { get; set; }
    }

    private readonly struct HiddenArgumentProbe;

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
    public void DependencyArrayFactoryFallsBackWhenSingletonLifetimeIsReplaced()
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
    public void DependencyFillingIsLazy()
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

    [Theory]
    [InlineData(typeof(ReflectionArity0Component), 0)]
    [InlineData(typeof(ReflectionArity1Component), 1)]
    [InlineData(typeof(ReflectionArity2Component), 2)]
    [InlineData(typeof(ReflectionArity3Component), 3)]
    [InlineData(typeof(ReflectionArity4Component), 4)]
    [InlineData(typeof(ReflectionArity5Component), 5)]
    public void ReflectionPathResolvesEachArity(Type componentType, int arity)
    {
        // Arrange
        Type[] leaves = [typeof(ReflectionLeaf1), typeof(ReflectionLeaf2), typeof(ReflectionLeaf3), typeof(ReflectionLeaf4), typeof(ReflectionLeaf5)];
        IServiceCollection services = new ServiceCollection();
        foreach (var leaf in leaves)
        {
            services.Add(ServiceDescriptor.Describe(leaf, leaf, ServiceLifetime.Transient));
        }
        services.Add(ServiceDescriptor.Describe(componentType, componentType, ServiceLifetime.Transient));
        using var provider = services.BuildGeneratedServiceProvider();

        // Act
        var component = (IReflectionArityComponent)((IServiceProvider)provider).GetRequiredService(componentType);

        // Assert
        Assert.Equal(ServiceFactoryStatus.RuntimeFallback, provider.CreateFactoryReport().First(x => x.ServiceType == componentType).Status);
        Assert.Equal(leaves.Take(arity), component.Arguments.Select(static x => x.GetType()));
    }

    [Fact]
    public void ReflectionPathInjectsProperties()
    {
        // Arrange
        IServiceCollection services = new ServiceCollection();
        services.Add(ServiceDescriptor.Describe(typeof(ReflectionLeaf1), typeof(ReflectionLeaf1), ServiceLifetime.Singleton));
        services.Add(ServiceDescriptor.Describe(typeof(ReflectionPropertyComponent), typeof(ReflectionPropertyComponent), ServiceLifetime.Transient));
        using var provider = services.BuildGeneratedServiceProvider();

        // Act
        var component = provider.GetRequiredService<ReflectionPropertyComponent>();

        // Assert
        Assert.Equal(ServiceFactoryStatus.RuntimeFallback, provider.CreateFactoryReport().First(static x => x.ServiceType == typeof(ReflectionPropertyComponent)).Status);
        Assert.Same(provider.GetRequiredService<ReflectionLeaf1>(), component.Leaf);
    }

    [Fact]
    public void ReflectionPathPropertySetterThrowsOriginalException()
    {
        // Arrange
        IServiceCollection services = new ServiceCollection();
        services.Add(ServiceDescriptor.Describe(typeof(ReflectionLeaf1), typeof(ReflectionLeaf1), ServiceLifetime.Singleton));
        services.Add(ServiceDescriptor.Describe(typeof(ReflectionThrowingPropertyComponent), typeof(ReflectionThrowingPropertyComponent), ServiceLifetime.Transient));
        using var provider = services.BuildGeneratedServiceProvider();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(provider.GetRequiredService<ReflectionThrowingPropertyComponent>);
    }

    [Fact]
    public void ReflectionPathPostConstructThrowsOriginalException()
    {
        // Arrange
        GeneratedFactoryRegistry.RegisterInitializer(typeof(ReflectionThrowingInitComponent), nameof(ReflectionThrowingInitComponent.Setup));
        IServiceCollection services = new ServiceCollection();
        services.Add(ServiceDescriptor.Describe(typeof(ReflectionThrowingInitComponent), typeof(ReflectionThrowingInitComponent), ServiceLifetime.Transient));
        using var provider = services.BuildGeneratedServiceProvider();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(provider.GetRequiredService<ReflectionThrowingInitComponent>);
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
