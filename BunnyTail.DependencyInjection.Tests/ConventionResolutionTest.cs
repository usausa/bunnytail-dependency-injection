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
        using var provider = CreateProvider();

        Assert.NotNull(provider.GetService<EchoService>());
    }

    [Fact]
    public void InterfaceRegistrationWhenSingleInterface()
    {
        using var provider = CreateProvider();

        Assert.IsType<BarService>(provider.GetRequiredService<IBarService>());
        Assert.Null(provider.GetService<BarService>());
    }

    [Fact]
    public void ForwardingRegistrationWhenMultipleInterfaces()
    {
        using var provider = CreateProvider();

        var self = provider.GetRequiredService<MixedService>();
        Assert.Same(self, provider.GetRequiredService<IMixed1>());
        Assert.Same(self, provider.GetRequiredService<IMixed2>());
    }
}
