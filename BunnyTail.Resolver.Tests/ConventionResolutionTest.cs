namespace BunnyTail.Resolver.Tests;

using BunnyTail.Resolver;
using BunnyTail.Resolver.Tests.Components;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

// 命名規約ベース登録メソッド (生成コード) の機能検証
public sealed class ConventionResolutionTest
{
    private static ResolverServiceProvider CreateProvider() =>
        new ServiceCollection().AddConventionServices().BuildResolverServiceProvider();

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
        Assert.Null(provider.GetService<BarService>());   // 1 インタフェースはインタフェース登録のみ
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
