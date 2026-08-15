namespace BunnyTail.Resolver.Tests;

using BunnyTail.Resolver;
using BunnyTail.Resolver.Tests.Components;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

// 手書き生成想定コード (ComponentRegistration.prototype.cs) 経由の機能検証
public sealed class ComponentResolutionTest
{
    private static ResolverServiceProvider CreateProvider() =>
        new ServiceCollection().AddComponents().BuildResolverServiceProvider();

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
        Assert.Same(a.Prop, b.Prop);   // PropDependency は Singleton
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

        Assert.Same(byClass, byInterface);   // フォワーディング登録で同一インスタンス
        Assert.NotSame(byInterface, other);  // スコープごとに別
    }

    [Fact]
    public void KeyedComponentIsResolved()
    {
        using var provider = CreateProvider();

        var service = provider.GetRequiredKeyedService<IKeyedService>("primary");

        Assert.IsType<PrimaryKeyedComponent>(service);
        Assert.Null(provider.GetService<IKeyedService>());   // 非 keyed では解決されない
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
}
