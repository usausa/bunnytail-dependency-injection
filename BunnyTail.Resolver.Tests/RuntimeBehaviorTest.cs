namespace BunnyTail.Resolver.Tests;

using BunnyTail.Resolver;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

// ランタイム挙動の検証: 並行解決の一意性と disposal の順序・伝播
// Verification of runtime behavior: uniqueness under concurrent resolution, and disposal ordering and propagation.
public sealed class RuntimeBehaviorTest
{
    // ---- 並行解決 / concurrent resolution ----

    public sealed class SlowSingleton
    {
        private static int created;

        public static int Created => created;

        public SlowSingleton()
        {
            Interlocked.Increment(ref created);
            Thread.Sleep(10);   // 初回生成を引き延ばして競合窓を広げる / widens the race window on first creation
        }

        public static void Reset() => created = 0;
    }

    public sealed class SlowScoped
    {
        private static int created;

        public static int Created => created;

        public SlowScoped()
        {
            Interlocked.Increment(ref created);
            Thread.Sleep(10);
        }

        public static void Reset() => created = 0;
    }

    public sealed class SlowDependency
    {
        public SlowDependency(SlowSingleton singleton)
        {
            Singleton = singleton;
        }

        public SlowSingleton Singleton { get; }
    }

    [Fact]
    public void ConcurrentFirstResolutionCreatesSingletonOnce()
    {
        SlowSingleton.Reset();
        using var provider = new ServiceCollection()
            .AddSingleton<SlowSingleton>()
            .BuildGeneratedServiceProvider();

        var results = new SlowSingleton[32];
        // Parallel.For は using スコープを抜ける前に完走するため、捕捉した provider は破棄されていない
        // Parallel.For completes before the using scope ends, so the captured provider is not disposed yet.
        // ReSharper disable once AccessToDisposedClosure
        Parallel.For(0, results.Length, i => results[i] = provider.GetRequiredService<SlowSingleton>());

        Assert.Equal(1, SlowSingleton.Created);
        Assert.All(results, x => Assert.Same(results[0], x));
    }

    [Fact]
    public void ConcurrentFirstResolutionCreatesScopedOncePerScope()
    {
        SlowScoped.Reset();
        using var provider = new ServiceCollection()
            .AddScoped<SlowScoped>()
            .BuildGeneratedServiceProvider();

        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var first = new SlowScoped[16];
        var second = new SlowScoped[16];
        // Parallel.For は using スコープを抜ける前に完走するため、捕捉した provider は破棄されていない
        // Parallel.For completes before the using scope ends, so the captured provider is not disposed yet.
        // ReSharper disable once AccessToDisposedClosure
        Parallel.For(0, 32, i =>
        {
            if (i < 16)
            {
                first[i] = scope1.ServiceProvider.GetRequiredService<SlowScoped>();
            }
            else
            {
                second[i - 16] = scope2.ServiceProvider.GetRequiredService<SlowScoped>();
            }
        });

        // スコープごとに 1 つ = 合計 2 つ / one per scope, two in total
        Assert.Equal(2, SlowScoped.Created);
        Assert.All(first, x => Assert.Same(first[0], x));
        Assert.All(second, x => Assert.Same(second[0], x));
        Assert.NotSame(first[0], second[0]);
    }

    [Fact]
    public void ConcurrentResolutionOfDependentGraphSharesSingleton()
    {
        SlowSingleton.Reset();
        using var provider = new ServiceCollection()
            .AddSingleton<SlowSingleton>()
            .AddTransient<SlowDependency>()
            .BuildGeneratedServiceProvider();

        // 同じ singleton を直接解決する側と依存経由で受け取る側を同時に走らせる
        // Resolves the same singleton directly and through a dependency at the same time.
        var direct = new SlowSingleton[16];
        var indirect = new SlowDependency[16];
        // Parallel.For は using スコープを抜ける前に完走するため、捕捉した provider は破棄されていない
        // Parallel.For completes before the using scope ends, so the captured provider is not disposed yet.
        // ReSharper disable once AccessToDisposedClosure
        Parallel.For(0, 32, i =>
        {
            if (i < 16)
            {
                direct[i] = provider.GetRequiredService<SlowSingleton>();
            }
            else
            {
                indirect[i - 16] = provider.GetRequiredService<SlowDependency>();
            }
        });

        Assert.Equal(1, SlowSingleton.Created);
        Assert.All(direct, x => Assert.Same(direct[0], x));
        Assert.All(indirect, x => Assert.Same(direct[0], x.Singleton));
    }

    // ---- disposal ----

    private sealed class TrackedDisposable : IDisposable
    {
        private readonly List<string> log;

        private readonly string name;

        public TrackedDisposable(List<string> log, string name)
        {
            this.log = log;
            this.name = name;
        }

        public void Dispose()
        {
            lock (log)
            {
                log.Add(name);
            }
        }
    }

    private sealed class TrackedAsyncDisposable : IAsyncDisposable
    {
        private readonly List<string> log;

        private readonly string name;

        public TrackedAsyncDisposable(List<string> log, string name)
        {
            this.log = log;
            this.name = name;
        }

        public ValueTask DisposeAsync()
        {
            lock (log)
            {
                log.Add(name);
            }

            return default;
        }
    }

    [Fact]
    public void ScopedDisposablesAreDisposedInReverseCreationOrder()
    {
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddScoped(_ => new TrackedDisposable(log, "first"));
        services.AddScoped<IDisposable>(_ => new TrackedDisposable(log, "second"));
        using var provider = services.BuildGeneratedServiceProvider();

        var scope = provider.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<TrackedDisposable>();
        _ = scope.ServiceProvider.GetRequiredService<IDisposable>();
        scope.Dispose();

        Assert.Equal(["second", "first"], log);
    }

    [Fact]
    public void TransientDisposablesAreTrackedByTheResolvingScope()
    {
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddTransient(_ => new TrackedDisposable(log, "transient"));
        using var provider = services.BuildGeneratedServiceProvider();

        var scope = provider.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<TrackedDisposable>();
        _ = scope.ServiceProvider.GetRequiredService<TrackedDisposable>();

        // スコープ破棄で解決した数だけ破棄される (root は無関係)
        // Disposing the scope disposes exactly the instances it resolved; the root is unaffected.
        Assert.Empty(log);
        scope.Dispose();
        Assert.Equal(["transient", "transient"], log);
    }

    [Fact]
    public void SingletonDisposablesAreDisposedWithTheRootProvider()
    {
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(_ => new TrackedDisposable(log, "singleton"));
        var provider = services.BuildGeneratedServiceProvider();

        using (var scope = provider.CreateScope())
        {
            _ = scope.ServiceProvider.GetRequiredService<TrackedDisposable>();
        }

        // スコープ経由で解決しても singleton は root が所有する
        // Singletons stay owned by the root even when resolved through a scope.
        Assert.Empty(log);
        provider.Dispose();
        Assert.Equal(["singleton"], log);
    }

    [Fact]
    public async Task AsyncDisposablesAreDisposedInReverseCreationOrder()
    {
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddScoped(_ => new TrackedAsyncDisposable(log, "first"));
        services.AddScoped(_ => new TrackedDisposable(log, "second"));
        await using var provider = services.BuildGeneratedServiceProvider();

        var scope = ((IServiceProvider)provider).CreateAsyncScope();
        _ = scope.ServiceProvider.GetRequiredService<TrackedAsyncDisposable>();
        _ = scope.ServiceProvider.GetRequiredService<TrackedDisposable>();
        await scope.DisposeAsync();

        // 同期のみ実装の要素も DisposeAsync 経路で破棄される
        // Elements implementing only IDisposable are still disposed on the DisposeAsync path.
        Assert.Equal(["second", "first"], log);
    }

    [Fact]
    public void DisposingScopeTwiceDisposesInstancesOnce()
    {
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddScoped(_ => new TrackedDisposable(log, "scoped"));
        using var provider = services.BuildGeneratedServiceProvider();

        var scope = provider.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<TrackedDisposable>();
        scope.Dispose();
        scope.Dispose();

        Assert.Equal(["scoped"], log);
    }

    [Fact]
    public void ResolvingFromDisposedScopeThrows()
    {
        using var provider = new ServiceCollection()
            .AddTransient<SlowDependency>()
            .AddSingleton<SlowSingleton>()
            .BuildGeneratedServiceProvider();

        var scope = provider.CreateScope();
        scope.Dispose();

        Assert.Throws<ObjectDisposedException>(scope.ServiceProvider.GetRequiredService<SlowDependency>);
    }

    [Fact]
    public void ConcurrentDisposalOfScopesIsIsolated()
    {
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddScoped(_ => new TrackedDisposable(log, "scoped"));
        using var provider = services.BuildGeneratedServiceProvider();

        // スコープの生成・解決・破棄を並行に回し、破棄漏れ・二重破棄が出ないこと
        // Creates, resolves and disposes scopes concurrently; nothing is missed or disposed twice.
        // Parallel.For は using スコープを抜ける前に完走するため、捕捉した provider は破棄されていない
        // Parallel.For completes before the using scope ends, so the captured provider is not disposed yet.
        // ReSharper disable once AccessToDisposedClosure
        Parallel.For(0, 32, index =>
        {
            _ = index;
            using var scope = provider.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<TrackedDisposable>();
        });

        Assert.Equal(32, log.Count);
    }
}
