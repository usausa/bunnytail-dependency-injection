namespace BunnyTail.DependencyInjection.Tests;

using System.Runtime.CompilerServices;

using BunnyTail.DependencyInjection;
using BunnyTail.DependencyInjection.Diagnostics;

using Microsoft.Extensions.DependencyInjection;

public sealed class DisposableTrackingTests
{
    private static GeneratedServiceProvider CreateGeneratedProvider(Action<GeneratedServiceProviderOptions>? configure = null) =>
        configure is null
            ? new ServiceCollection().AddGeneratedComponents().BuildGeneratedServiceProvider()
            : new ServiceCollection().AddGeneratedComponents().BuildGeneratedServiceProvider(configure);

    //--------------------------------------------------------------------------------
    // Global option
    //--------------------------------------------------------------------------------

    [Fact]
    public void DefaultTracksTransientDisposable()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTransient<TrackingDisposable>();
        var provider = services.BuildGeneratedServiceProvider();

        // Act
        var instance = provider.GetRequiredService<TrackingDisposable>();
        provider.Dispose();

        // Assert
        Assert.Equal(1, instance.DisposeCount);
    }

    [Fact]
    public void GlobalDisableSkipsTransientDisposal()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTransient<TrackingDisposable>();
        var provider = services.BuildGeneratedServiceProvider(static o => o.TrackTransientDisposables = false);

        // Act
        var rootInstance = provider.GetRequiredService<TrackingDisposable>();

        var scope = provider.CreateScope();
        var scopedInstance = scope.ServiceProvider.GetRequiredService<TrackingDisposable>();
        scope.Dispose();

        provider.Dispose();

        // Assert
        Assert.Equal(0, rootInstance.DisposeCount);
        Assert.Equal(0, scopedInstance.DisposeCount);
    }

    [Fact]
    public void GlobalDisableKeepsSingletonAndScopedDisposal()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<TrackingDisposable>();
        services.AddScoped<ScopedTrackingDisposable>();
        var provider = services.BuildGeneratedServiceProvider(static o => o.TrackTransientDisposables = false);

        // Act
        var singleton = provider.GetRequiredService<TrackingDisposable>();

        var scope = provider.CreateScope();
        var scoped = scope.ServiceProvider.GetRequiredService<ScopedTrackingDisposable>();
        scope.Dispose();

        // Assert
        Assert.Equal(1, scoped.DisposeCount);

        // Act
        provider.Dispose();

        // Assert
        Assert.Equal(1, singleton.DisposeCount);
    }

    [Fact]
    public void GlobalDisableSkipsFactoryRegistration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTransient<TrackingDisposable>(static _ => new TrackingDisposable());
        var provider = services.BuildGeneratedServiceProvider(static o => o.TrackTransientDisposables = false);

        // Act
        var instance = provider.GetRequiredService<TrackingDisposable>();
        provider.Dispose();

        // Assert
        Assert.Equal(0, instance.DisposeCount);
    }

    [Fact]
    public void GlobalDisableSkipsRuntimeFallbackRegistration()
    {
        // Arrange
        IServiceCollection services = new ServiceCollection();
        services.Add(new ServiceDescriptor(typeof(TrackingFallbackDisposable), typeof(TrackingFallbackDisposable), ServiceLifetime.Transient));
        var provider = services.BuildGeneratedServiceProvider(static o => o.TrackTransientDisposables = false);

        // Act
        var instance = provider.GetRequiredService<TrackingFallbackDisposable>();

        // Assert: the registration must run on the runtime fallback path, not on a generated factory
        Assert.Contains(
            provider.CreateFactoryReport(),
            static x => (x.ImplementationType == typeof(TrackingFallbackDisposable)) && (x.Status == ServiceFactoryStatus.RuntimeFallback));

        // Act
        provider.Dispose();

        // Assert
        Assert.Equal(0, instance.DisposeCount);
    }

    [Fact]
    public void GlobalDisableDoesNotRetainTransient()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTransient<TrackingDisposable>();
        using var provider = services.BuildGeneratedServiceProvider(static o => o.TrackTransientDisposables = false);

        // Act
        var references = CreateAndRelease(provider);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Assert
        Assert.DoesNotContain(references, static x => x.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] CreateAndRelease(GeneratedServiceProvider provider)
    {
        var references = new WeakReference[16];
        for (var i = 0; i < references.Length; i++)
        {
            references[i] = new WeakReference(provider.GetRequiredService<TrackingDisposable>());
        }

        return references;
    }

    //--------------------------------------------------------------------------------
    // Attribute
    //--------------------------------------------------------------------------------

    [Fact]
    public void AttributeDisabledOverridesDefault()
    {
        // Arrange
        var provider = CreateGeneratedProvider();

        // Act
        var instance = provider.GetRequiredService<TrackingUntrackedComponent>();

        // Assert: the component must run on its generated factory
        Assert.Contains(
            provider.CreateFactoryReport(),
            static x => (x.ImplementationType == typeof(TrackingUntrackedComponent)) && (x.Status == ServiceFactoryStatus.Generated));

        // Act
        provider.Dispose();

        // Assert
        Assert.Equal(0, instance.DisposeCount);
    }

    [Fact]
    public void AttributeEnabledOverridesGlobalDisable()
    {
        // Arrange
        var provider = CreateGeneratedProvider(static o => o.TrackTransientDisposables = false);

        // Act
        var tracked = provider.GetRequiredService<TrackingEnabledComponent>();
        var untracked = provider.GetRequiredService<TrackingUntrackedComponent>();
        provider.Dispose();

        // Assert
        Assert.Equal(1, tracked.DisposeCount);
        Assert.Equal(0, untracked.DisposeCount);
    }

    [Fact]
    public void AttributeDisabledAppliesToInterfaceForwarding()
    {
        // Arrange
        var provider = CreateGeneratedProvider();

        // Act
        var instance = provider.GetRequiredService<ITrackingForwardProbe>();
        provider.Dispose();

        // Assert
        Assert.Equal(0, ((TrackingForwardComponent)instance).DisposeCount);
    }

    //--------------------------------------------------------------------------------
    // Add overloads
    //--------------------------------------------------------------------------------

    [Fact]
    public void AddOverloadDisabledOverridesDefault()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTransient<TrackingDisposable>(DisposableTracking.Disabled);
        var provider = services.BuildGeneratedServiceProvider();

        // Act
        var instance = provider.GetRequiredService<TrackingDisposable>();
        provider.Dispose();

        // Assert
        Assert.Equal(0, instance.DisposeCount);
    }

    [Fact]
    public void AddOverloadEnabledOverridesGlobalDisable()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTransient<TrackingDisposable>(DisposableTracking.Enabled);
        var provider = services.BuildGeneratedServiceProvider(static o => o.TrackTransientDisposables = false);

        // Act
        var instance = provider.GetRequiredService<TrackingDisposable>();
        provider.Dispose();

        // Assert
        Assert.Equal(1, instance.DisposeCount);
    }

    [Fact]
    public void FactoryOverloadDisabledOverridesDefault()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTransient(static _ => new TrackingDisposable(), DisposableTracking.Disabled);
        var provider = services.BuildGeneratedServiceProvider();

        // Act
        var instance = provider.GetRequiredService<TrackingDisposable>();
        provider.Dispose();

        // Assert
        Assert.Equal(0, instance.DisposeCount);
    }

    [Fact]
    public void KeyedOverloadDisabledOverridesDefault()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddKeyedTransient<TrackingDisposable>("tracking", DisposableTracking.Disabled);
        var provider = services.BuildGeneratedServiceProvider();

        // Act
        var instance = provider.GetRequiredKeyedService<TrackingDisposable>("tracking");
        provider.Dispose();

        // Assert
        Assert.Equal(0, instance.DisposeCount);
    }

    //--------------------------------------------------------------------------------
    // Type override
    //--------------------------------------------------------------------------------

    [Fact]
    public void TypeOverrideDisablesTracking()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTransient<TrackingDisposable>();
        var provider = services.BuildGeneratedServiceProvider(static o => o.DisableTracking(typeof(TrackingDisposable)));

        // Act
        var instance = provider.GetRequiredService<TrackingDisposable>();
        provider.Dispose();

        // Assert
        Assert.Equal(0, instance.DisposeCount);
    }

    [Fact]
    public void TypeOverrideEnablesTrackingUnderGlobalDisable()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTransient<TrackingDisposable>();
        var provider = services.BuildGeneratedServiceProvider(static o =>
        {
            o.TrackTransientDisposables = false;
            o.EnableTracking(typeof(TrackingDisposable));
        });

        // Act
        var instance = provider.GetRequiredService<TrackingDisposable>();
        provider.Dispose();

        // Assert
        Assert.Equal(1, instance.DisposeCount);
    }

    [Fact]
    public void DescriptorSettingBeatsTypeOverride()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTransient<TrackingDisposable>(DisposableTracking.Enabled);
        var provider = services.BuildGeneratedServiceProvider(static o =>
        {
            o.TrackTransientDisposables = false;
            o.DisableTracking(typeof(TrackingDisposable));
        });

        // Act
        var instance = provider.GetRequiredService<TrackingDisposable>();
        provider.Dispose();

        // Assert
        Assert.Equal(1, instance.DisposeCount);
    }
}

public sealed class TrackingDisposable : IDisposable
{
    public int DisposeCount { get; private set; }

    public void Dispose() => DisposeCount++;
}

public sealed class ScopedTrackingDisposable : IDisposable
{
    public int DisposeCount { get; private set; }

    public void Dispose() => DisposeCount++;
}

public sealed class TrackingFallbackDisposable : IDisposable
{
    public int DisposeCount { get; private set; }

    public void Dispose() => DisposeCount++;
}

[Transient(Tracking = DisposableTracking.Disabled)]
public sealed class TrackingUntrackedComponent : IDisposable
{
    public int DisposeCount { get; private set; }

    public void Dispose() => DisposeCount++;
}

[Transient(Tracking = DisposableTracking.Enabled)]
public sealed class TrackingEnabledComponent : IDisposable
{
    public int DisposeCount { get; private set; }

    public void Dispose() => DisposeCount++;
}

public interface ITrackingForwardProbe;

[Transient(Tracking = DisposableTracking.Disabled, WithInterfaces = true)]
public sealed class TrackingForwardComponent : ITrackingForwardProbe, IDisposable
{
    public int DisposeCount { get; private set; }

    public void Dispose() => DisposeCount++;
}
