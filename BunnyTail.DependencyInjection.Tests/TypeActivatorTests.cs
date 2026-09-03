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
