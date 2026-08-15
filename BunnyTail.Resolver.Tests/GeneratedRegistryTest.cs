namespace BunnyTail.Resolver.Tests;

using BunnyTail.Resolver;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

// 生成経路フックの検証: コンストラクタ前提が一致すれば生成ファクトリが使われ、
// 不一致ならリフレクション経路へフォールバックする (SPEC 2.3)
public sealed class GeneratedRegistryTest
{
    public sealed class HookComponent
    {
        public bool CreatedByGeneratedFactory { get; init; }
    }

    public sealed class MismatchComponent
    {
        public MismatchComponent(HookComponent dependency)
        {
            _ = dependency;
        }
    }

    public sealed class KeyedHookComponent
    {
        public object? ReceivedKey { get; init; }
    }

    static GeneratedRegistryTest()
    {
        // 前提一致: HookComponent は引数なしコンストラクタ
        GeneratedComponentRegistry.Register(
            typeof(HookComponent),
            Type.EmptyTypes,
            static _ => new HookComponent { CreatedByGeneratedFactory = true });

        // 前提不一致: MismatchComponent の実コンストラクタは (HookComponent) だが、引数なしと登録
        GeneratedComponentRegistry.Register(
            typeof(MismatchComponent),
            Type.EmptyTypes,
            static _ => throw new InvalidOperationException("生成ファクトリが誤って使用された"));

        // keyed 生成ファクトリ (key を受け取る)
        GeneratedComponentRegistry.RegisterKeyed(
            typeof(KeyedHookComponent),
            Type.EmptyTypes,
            static (_, key) => new KeyedHookComponent { ReceivedKey = key });
    }

    [Fact]
    public void GeneratedFactoryIsUsedWhenConstructorMatches()
    {
        using var provider = new ServiceCollection()
            .AddTransient<HookComponent>()
            .BuildResolverServiceProvider();

        var instance = provider.GetRequiredService<HookComponent>();

        Assert.True(instance.CreatedByGeneratedFactory);
    }

    [Fact]
    public void FallsBackToReflectionWhenConstructorMismatches()
    {
        using var provider = new ServiceCollection()
            .AddTransient<HookComponent>()
            .AddTransient<MismatchComponent>()
            .BuildResolverServiceProvider();

        // 生成ファクトリ (throw する) ではなくリフレクション経路が使われること
        var instance = provider.GetRequiredService<MismatchComponent>();

        Assert.NotNull(instance);
    }

    [Fact]
    public void KeyedGeneratedFactoryIsUsedAndReceivesKey()
    {
        using var provider = new ServiceCollection()
            .AddKeyedTransient<KeyedHookComponent>("first")
            .AddKeyedTransient<KeyedHookComponent>(KeyedService.AnyKey)
            .BuildResolverServiceProvider();

        var exact = provider.GetRequiredKeyedService<KeyedHookComponent>("first");
        Assert.Equal("first", exact.ReceivedKey);

        // AnyKey 登録経由でも要求キーが渡る
        var derived = provider.GetRequiredKeyedService<KeyedHookComponent>("something-else");
        Assert.Equal("something-else", derived.ReceivedKey);
    }

    [Fact]
    public void FactoryRegistrationIsNotAffectedByGeneratedFactory()
    {
        // ImplementationFactory 登録は生成ファクトリより優先される (ユーザー指定が勝つ)
        using var provider = new ServiceCollection()
            .AddTransient(static _ => new HookComponent { CreatedByGeneratedFactory = false })
            .BuildResolverServiceProvider();

        var instance = provider.GetRequiredService<HookComponent>();

        Assert.False(instance.CreatedByGeneratedFactory);
    }
}
