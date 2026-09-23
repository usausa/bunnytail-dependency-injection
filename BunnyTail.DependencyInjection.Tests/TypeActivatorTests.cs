namespace BunnyTail.DependencyInjection.Tests;

using BunnyTail.DependencyInjection;
using BunnyTail.DependencyInjection.Diagnostics;

using Microsoft.Extensions.DependencyInjection;

public sealed class TypeActivatorTests
{
    //--------------------------------------------------------------------------------
    // Activation
    //--------------------------------------------------------------------------------

    [Fact]
    public void ActivateCreatesCallerOwnedInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ActivationDependency>();
        var provider = services.BuildGeneratedServiceProvider();

        // Act
        var first = provider.Activate<ActivationTarget>();
        var second = provider.Activate<ActivationTarget>();
        provider.Dispose();

        // Assert
        Assert.NotSame(first, second);
        Assert.Equal(0, first.DisposeCount);
        Assert.Equal(0, second.DisposeCount);
    }

    [Fact]
    public void ActivateInjectsDependencies()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ActivationDependency>();
        using var provider = services.BuildGeneratedServiceProvider();

        // Act
        var instance = provider.Activate<ActivationTarget>();

        // Assert
        Assert.Same(provider.GetRequiredService<ActivationDependency>(), instance.Dependency);
        Assert.Same(provider.GetRequiredService<ActivationDependency>(), instance.Injected);
        Assert.True(instance.Initialized);
    }

    [Fact]
    public void ActivateFromScopeUsesScopedDependencies()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<ActivationScopedDependency>();
        using var provider = services.BuildGeneratedServiceProvider();

        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();
        var activator1 = (ServiceProviderScope)scope1.ServiceProvider;
        var activator2 = (ServiceProviderScope)scope2.ServiceProvider;

        // Act
        var instance1 = activator1.Activate<ActivationScopedTarget>();
        var instance2 = activator2.Activate<ActivationScopedTarget>();

        // Assert
        Assert.Same(scope1.ServiceProvider.GetRequiredService<ActivationScopedDependency>(), instance1.Dependency);
        Assert.Same(scope2.ServiceProvider.GetRequiredService<ActivationScopedDependency>(), instance2.Dependency);
        Assert.NotSame(instance1.Dependency, instance2.Dependency);
    }

    [Fact]
    public void ActivateIgnoresRegistrations()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ActivationRegisteredSingleton>();
        using var provider = services.BuildGeneratedServiceProvider();

        // Act
        var registered = provider.GetRequiredService<ActivationRegisteredSingleton>();
        var activated = provider.Activate<ActivationRegisteredSingleton>();

        // Assert
        Assert.NotSame(registered, activated);
        Assert.Same(registered, provider.GetRequiredService<ActivationRegisteredSingleton>());
    }

    //--------------------------------------------------------------------------------
    // Factory paths
    //--------------------------------------------------------------------------------

    [Fact]
    public void GenericCallSiteRunsOnGeneratedFactory()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ActivationDependency>();
        using var provider = services.BuildGeneratedServiceProvider();

        // Act: the generic call site itself drives factory generation
        _ = provider.Activate<ActivationTarget>();

        // Assert
        Assert.Contains(
            provider.CreateFactoryReport(),
            static x => (x.ImplementationType == typeof(ActivationTarget)) && (x.Status == ServiceFactoryStatus.Generated));
    }

    [Fact]
    public void UncollectedTypeRunsOnRuntimeFallback()
    {
        // Arrange
        using var provider = new ServiceCollection().BuildGeneratedServiceProvider();

        // Act: a Type variable cannot be collected at build time
        var type = typeof(ActivationFallbackTarget);
        var instance = provider.Activate(type);

        // Assert
        Assert.IsType<ActivationFallbackTarget>(instance);
        Assert.Contains(
            provider.CreateFactoryReport(),
            static x => (x.ImplementationType == typeof(ActivationFallbackTarget)) && (x.Status == ServiceFactoryStatus.RuntimeFallback));
    }

    //--------------------------------------------------------------------------------
    // Activation table
    //--------------------------------------------------------------------------------

    // Distinct closed generic types are cheap to make and each one is a separate activation entry
    private static Type[] CreateManyActivationTypes(int count)
    {
        Type[] arguments =
        [
            typeof(bool), typeof(byte), typeof(sbyte), typeof(char), typeof(short), typeof(ushort), typeof(int), typeof(uint),
            typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal), typeof(string), typeof(object), typeof(Guid),
            typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan), typeof(DateOnly), typeof(TimeOnly), typeof(Uri), typeof(Version), typeof(Half),
            typeof(Int128), typeof(UInt128), typeof(nint), typeof(nuint), typeof(Exception), typeof(Type), typeof(Random), typeof(Attribute),
            typeof(List<int>), typeof(List<string>), typeof(Dictionary<int, int>), typeof(HashSet<int>), typeof(Queue<int>), typeof(Stack<int>), typeof(int[]), typeof(string[])
        ];
        return [.. arguments.Take(count).Select(static x => typeof(GenericActivationTarget<>).MakeGenericType(x))];
    }

    [Fact]
    public void ActivateManyTypesKeepsEveryEntryAcrossTableGrowth()
    {
        // Arrange: far more distinct types than the initial activation table holds, so it is rebuilt several times
        using var provider = new ServiceCollection().BuildGeneratedServiceProvider();
        var types = CreateManyActivationTypes(40);

        // Act
        var first = types.Select(provider.Activate).ToArray();
        var second = types.Select(provider.Activate).ToArray();

        // Assert: every type activates before and after the rebuilds, and each one has exactly one accessor
        for (var i = 0; i < types.Length; i++)
        {
            Assert.IsType(types[i], first[i]);
            Assert.IsType(types[i], second[i]);
            Assert.NotSame(first[i], second[i]);
        }

        var report = provider.CreateFactoryReport();
        Assert.All(types, type => Assert.Single(report, x => x.ServiceType == type));
    }

    [Fact]
    public void ConcurrentActivationPublishesOneAccessorPerType()
    {
        // Arrange
        using var provider = new ServiceCollection().BuildGeneratedServiceProvider();
        var types = CreateManyActivationTypes(24);
        var results = new object[types.Length * 8];

        // Act: every type is activated for the first time from several threads at once
        // ReSharper disable once AccessToDisposedClosure
        Parallel.For(0, results.Length, i => results[i] = provider.Activate(types[i % types.Length]));

        // Assert
        for (var i = 0; i < results.Length; i++)
        {
            Assert.IsType(types[i % types.Length], results[i]);
        }

        var report = provider.CreateFactoryReport();
        Assert.All(types, type => Assert.Single(report, x => x.ServiceType == type));
    }

    //--------------------------------------------------------------------------------
    // Injected activator
    //--------------------------------------------------------------------------------

    [Fact]
    public void ActivatorResolvesAsScopeItself()
    {
        // Arrange
        using var provider = new ServiceCollection().BuildGeneratedServiceProvider();
        using var scope = provider.CreateScope();

        // Act & Assert
        Assert.Same(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<ITypeActivator>());
    }

    [Fact]
    public void InjectedActivatorIsScopeAware()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<ActivationScopedDependency>();
        services.AddTransient<ActivationConsumer>();
        using var provider = services.BuildGeneratedServiceProvider();

        using var scope = provider.CreateScope();
        var consumer = scope.ServiceProvider.GetRequiredService<ActivationConsumer>();

        // Act
        var instance = consumer.Activator.Activate<ActivationScopedTarget>();

        // Assert
        Assert.Same(scope.ServiceProvider.GetRequiredService<ActivationScopedDependency>(), instance.Dependency);
    }

    //--------------------------------------------------------------------------------
    // Errors
    //--------------------------------------------------------------------------------

    [Fact]
    public void ActivateRejectsNonConcreteTypes()
    {
        // Arrange
        using var provider = new ServiceCollection().BuildGeneratedServiceProvider();

        // Act & Assert
        var interfaceType = typeof(IActivationAbstraction);
        var abstractType = typeof(ActivationAbstractTarget);
        Assert.Throws<InvalidOperationException>(() => provider.Activate(interfaceType));
        Assert.Throws<InvalidOperationException>(() => provider.Activate(abstractType));
    }

    [Fact]
    public void ActivateThrowsAfterDispose()
    {
        // Arrange
        var provider = new ServiceCollection().BuildGeneratedServiceProvider();
        provider.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(provider.Activate<ActivationDependency>);
    }
}

public sealed class ActivationDependency;

public sealed class ActivationScopedDependency;

public sealed class ActivationTarget : IInitializable, IDisposable
{
    public ActivationDependency Dependency { get; }

    [Inject]
    public ActivationDependency Injected { get; set; } = default!;

    public bool Initialized { get; private set; }

    public int DisposeCount { get; private set; }

    public ActivationTarget(ActivationDependency dependency)
    {
        Dependency = dependency;
    }

    public void Initialize() => Initialized = true;

    public void Dispose() => DisposeCount++;
}

public sealed class ActivationScopedTarget
{
    public ActivationScopedDependency Dependency { get; }

    public ActivationScopedTarget(ActivationScopedDependency dependency)
    {
        Dependency = dependency;
    }
}

public sealed class ActivationFallbackTarget;

public sealed class ActivationRegisteredSingleton;

public sealed class ActivationConsumer
{
    public ITypeActivator Activator { get; }

    public ActivationConsumer(ITypeActivator activator)
    {
        Activator = activator;
    }
}

public interface IActivationAbstraction;

public abstract class ActivationAbstractTarget;

#pragma warning disable CA1812
// ReSharper disable once UnusedTypeParameter
public sealed class GenericActivationTarget<T>;
#pragma warning restore CA1812
