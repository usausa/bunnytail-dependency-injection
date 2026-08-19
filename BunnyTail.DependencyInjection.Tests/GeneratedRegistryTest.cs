namespace BunnyTail.DependencyInjection.Tests;

using BunnyTail.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

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
        // Matching assumption: HookComponent has a parameterless constructor
        GeneratedComponentRegistry.Register(
            typeof(HookComponent),
            Type.EmptyTypes,
            static _ => new HookComponent { CreatedByGeneratedFactory = true });

        // Mismatching assumption: MismatchComponent's real constructor is (HookComponent) but registered as parameterless
        GeneratedComponentRegistry.Register(
            typeof(MismatchComponent),
            Type.EmptyTypes,
            static _ => throw new InvalidOperationException("生成ファクトリが誤って使用された"));

        // Keyed generated factory
        GeneratedComponentRegistry.RegisterKeyed(
            typeof(KeyedHookComponent),
            Type.EmptyTypes,
            static (_, key) => new KeyedHookComponent { ReceivedKey = key });

        // Inline assumption: registered assuming InlineDependencyComponent resolves as a transient through its generated factory
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
            .BuildGeneratedServiceProvider();

        var instance = provider.GetRequiredService<HookComponent>();

        Assert.True(instance.CreatedByGeneratedFactory);
    }

    [Fact]
    public void FallsBackToReflectionWhenConstructorMismatches()
    {
        using var provider = new ServiceCollection()
            .AddTransient<HookComponent>()
            .AddTransient<MismatchComponent>()
            .BuildGeneratedServiceProvider();

        // The reflection path must be used instead of the generated factory
        var instance = provider.GetRequiredService<MismatchComponent>();

        Assert.NotNull(instance);
    }

    [Fact]
    public void KeyedGeneratedFactoryIsUsedAndReceivesKey()
    {
        using var provider = new ServiceCollection()
            .AddKeyedTransient<KeyedHookComponent>("first")
            .AddKeyedTransient<KeyedHookComponent>(KeyedService.AnyKey)
            .BuildGeneratedServiceProvider();

        var exact = provider.GetRequiredKeyedService<KeyedHookComponent>("first");
        Assert.Equal("first", exact.ReceivedKey);

        // The requested key is passed through even via the AnyKey registration
        var derived = provider.GetRequiredKeyedService<KeyedHookComponent>("something-else");
        Assert.Equal("something-else", derived.ReceivedKey);
    }

    [Fact]
    public void InlinedFactoryIsUsedWhenAssumptionHolds()
    {
        using var provider = new ServiceCollection()
            .AddTransient<InlineDependencyComponent>()
            .AddTransient<InlineRootComponent>()
            .BuildGeneratedServiceProvider();

        var instance = provider.GetRequiredService<InlineRootComponent>();

        Assert.True(instance.CreatedByGeneratedFactory);
    }

    [Fact]
    public void InlinedFactoryFallsBackWhenDependencyLifetimeDiffers()
    {
        // Assumed transient but registered as singleton at runtime
        using var provider = new ServiceCollection()
            .AddSingleton<InlineDependencyComponent>()
            .AddTransient<InlineRootComponent>()
            .BuildGeneratedServiceProvider();

        var instance = provider.GetRequiredService<InlineRootComponent>();

        Assert.False(instance.CreatedByGeneratedFactory);
    }

    [Fact]
    public void InlinedFactoryFallsBackWhenDependencyIsFactoryRegistered()
    {
        // The dependency was replaced by a user factory registration
        using var provider = new ServiceCollection()
            .AddTransient(static _ => new InlineDependencyComponent())
            .AddTransient<InlineRootComponent>()
            .BuildGeneratedServiceProvider();

        var instance = provider.GetRequiredService<InlineRootComponent>();

        Assert.False(instance.CreatedByGeneratedFactory);
    }

    [Fact]
    public void FactoryRegistrationIsNotAffectedByGeneratedFactory()
    {
        // ImplementationFactory registrations take precedence over generated factories
        using var provider = new ServiceCollection()
            .AddTransient(static _ => new HookComponent { CreatedByGeneratedFactory = false })
            .BuildGeneratedServiceProvider();

        var instance = provider.GetRequiredService<HookComponent>();

        Assert.False(instance.CreatedByGeneratedFactory);
    }
}
