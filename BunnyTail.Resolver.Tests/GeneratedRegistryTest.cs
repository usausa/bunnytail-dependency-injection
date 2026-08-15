namespace BunnyTail.Resolver.Tests;

using BunnyTail.Resolver;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

// 生成経路フックの検証: コンストラクタ前提が一致すれば生成ファクトリが使われ、不一致ならリフレクション経路へフォールバックする
// Verifies the generated path hook: the generated factory is used when the constructor assumption matches, otherwise it falls back to the reflection path.
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

    public sealed class InlineDependencyComponent
    {
    }

    public sealed class InlineRootComponent
    {
        public bool CreatedByGeneratedFactory { get; init; }
    }

    static GeneratedRegistryTest()
    {
        // 前提一致: HookComponent は引数なしコンストラクタ
        // Matching assumption: HookComponent has a parameterless constructor.
        GeneratedComponentRegistry.Register(
            typeof(HookComponent),
            Type.EmptyTypes,
            static _ => new HookComponent { CreatedByGeneratedFactory = true });

        // 前提不一致: MismatchComponent の実コンストラクタは (HookComponent) だが、引数なしと登録
        // Mismatching assumption: MismatchComponent's real constructor is (HookComponent) but registered as parameterless.
        GeneratedComponentRegistry.Register(
            typeof(MismatchComponent),
            Type.EmptyTypes,
            static _ => throw new InvalidOperationException("生成ファクトリが誤って使用された"));

        // keyed 生成ファクトリ (key を受け取る)
        // Keyed generated factory (receives the key).
        GeneratedComponentRegistry.RegisterKeyed(
            typeof(KeyedHookComponent),
            Type.EmptyTypes,
            static (_, key) => new KeyedHookComponent { ReceivedKey = key });

        // インライン展開前提付き: InlineDependencyComponent が「生成ファクトリによる transient 解決」になることを前提として登録
        // With an inline assumption: registered assuming InlineDependencyComponent resolves as a transient through its generated factory.
        GeneratedComponentRegistry.Register(
            typeof(InlineDependencyComponent),
            Type.EmptyTypes,
            static _ => new InlineDependencyComponent());

        GeneratedComponentRegistry.Register(
            typeof(InlineRootComponent),
            Type.EmptyTypes,
            [new InlinedDependency(typeof(InlineDependencyComponent), typeof(InlineDependencyComponent))],
            static _ => new InlineRootComponent { CreatedByGeneratedFactory = true });
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
        // The reflection path must be used instead of the generated factory (which throws).
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
        // The requested key is passed through even via the AnyKey registration.
        var derived = provider.GetRequiredKeyedService<KeyedHookComponent>("something-else");
        Assert.Equal("something-else", derived.ReceivedKey);
    }

    [Fact]
    public void InlinedFactoryIsUsedWhenAssumptionHolds()
    {
        using var provider = new ServiceCollection()
            .AddTransient<InlineDependencyComponent>()
            .AddTransient<InlineRootComponent>()
            .BuildResolverServiceProvider();

        var instance = provider.GetRequiredService<InlineRootComponent>();

        Assert.True(instance.CreatedByGeneratedFactory);
    }

    [Fact]
    public void InlinedFactoryFallsBackWhenDependencyLifetimeDiffers()
    {
        // 前提は transient だが実行時登録は singleton → リフレクション経路へフォールバック
        // Assumed transient but registered as singleton at runtime -> falls back to the reflection path.
        using var provider = new ServiceCollection()
            .AddSingleton<InlineDependencyComponent>()
            .AddTransient<InlineRootComponent>()
            .BuildResolverServiceProvider();

        var instance = provider.GetRequiredService<InlineRootComponent>();

        Assert.False(instance.CreatedByGeneratedFactory);
    }

    [Fact]
    public void InlinedFactoryFallsBackWhenDependencyIsFactoryRegistered()
    {
        // 依存がユーザーファクトリ登録に差し替えられた → 前提不成立でフォールバック
        // The dependency was replaced by a user factory registration -> assumption fails and falls back.
        using var provider = new ServiceCollection()
            .AddTransient(static _ => new InlineDependencyComponent())
            .AddTransient<InlineRootComponent>()
            .BuildResolverServiceProvider();

        var instance = provider.GetRequiredService<InlineRootComponent>();

        Assert.False(instance.CreatedByGeneratedFactory);
    }

    [Fact]
    public void FactoryRegistrationIsNotAffectedByGeneratedFactory()
    {
        // ImplementationFactory 登録は生成ファクトリより優先される (ユーザー指定が勝つ)
        // ImplementationFactory registrations take precedence over generated factories (user intent wins).
        using var provider = new ServiceCollection()
            .AddTransient(static _ => new HookComponent { CreatedByGeneratedFactory = false })
            .BuildResolverServiceProvider();

        var instance = provider.GetRequiredService<HookComponent>();

        Assert.False(instance.CreatedByGeneratedFactory);
    }
}
