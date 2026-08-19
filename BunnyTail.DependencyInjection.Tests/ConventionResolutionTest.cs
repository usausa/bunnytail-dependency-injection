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

    // 同一クラスに複数の登録メソッドがある場合、それぞれの本体が生成される
    // When one class holds several registration methods, every body is generated.
    [Fact]
    public void MultipleRegistrationMethodsOnSameClass()
    {
        using var provider = new ServiceCollection()
            .AddConventionServices()
            .AddConventionRepositories()
            .BuildGeneratedServiceProvider();

        Assert.NotNull(provider.GetService<EchoService>());
        Assert.NotNull(provider.GetService<SampleRepository>());
    }

    // public 以外のアクセシビリティで宣言した登録メソッドも生成される
    // Registration methods declared with a non-public accessibility are generated too.
    [Fact]
    public void NonPublicRegistrationMethodIsGenerated()
    {
        using var provider = new ServiceCollection()
            .AddConventionGadgetsThroughWrapper()
            .BuildGeneratedServiceProvider();

        Assert.NotNull(provider.GetService<SampleGadget>());
    }

    // DependencyInjectionIgnoreInterface で指定したインタフェースは登録されない (実装型の登録は残る)
    // Interfaces named by DependencyInjectionIgnoreInterface are not registered; the implementation registration remains.
    [Fact]
    public void IgnoredInterfaceIsNotRegistered()
    {
        using var provider = CreateProvider();

        Assert.NotNull(provider.GetService<IgnoredMarkerService>());
        Assert.Null(provider.GetService<IIgnoredMarker>());
    }
}
