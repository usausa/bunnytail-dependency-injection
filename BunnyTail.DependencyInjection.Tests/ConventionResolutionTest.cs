namespace BunnyTail.DependencyInjection.Tests;

using BunnyTail.DependencyInjection;
using BunnyTail.DependencyInjection.Tests.Components;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

public sealed class ConventionResolutionTest
{
    private static GeneratedServiceProvider CreateProvider() =>
        new ServiceCollection().AddConventionServices().BuildGeneratedServiceProvider();

    [Fact]
    public void SelfRegistrationWhenNoInterface()
    {
        // Arrange
        using var provider = CreateProvider();

        // Act & Assert
        Assert.NotNull(provider.GetService<EchoService>());
    }

    [Fact]
    public void InterfaceRegistrationWhenSingleInterface()
    {
        // Arrange
        using var provider = CreateProvider();

        // Act & Assert
        Assert.IsType<BarService>(provider.GetRequiredService<IBarService>());
        Assert.Null(provider.GetService<BarService>());
    }

    [Fact]
    public void ForwardingRegistrationWhenMultipleInterfaces()
    {
        // Arrange
        using var provider = CreateProvider();

        // Act
        var self = provider.GetRequiredService<MixedService>();

        // Assert
        Assert.Same(self, provider.GetRequiredService<IMixed1>());
        Assert.Same(self, provider.GetRequiredService<IMixed2>());
    }

    [Fact]
    public void MultipleRegistrationMethodsOnSameClass()
    {
        // Arrange
        using var provider = new ServiceCollection()
            .AddConventionServices()
            .AddConventionRepositories()
            .BuildGeneratedServiceProvider();

        // Act & Assert
        Assert.NotNull(provider.GetService<EchoService>());
        Assert.NotNull(provider.GetService<SampleRepository>());
    }

    [Fact]
    public void NonPublicRegistrationMethodIsGenerated()
    {
        // Arrange
        using var provider = new ServiceCollection()
            .AddConventionGadgetsThroughWrapper()
            .BuildGeneratedServiceProvider();

        // Act & Assert
        Assert.NotNull(provider.GetService<SampleGadget>());
    }

    [Fact]
    public void IgnoredInterfaceIsNotRegistered()
    {
        // Arrange
        using var provider = CreateProvider();

        // Act & Assert
        Assert.NotNull(provider.GetService<IgnoredMarkerService>());
        Assert.Null(provider.GetService<IIgnoredMarker>());
    }
}
