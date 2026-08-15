namespace BunnyTail.Resolver.Tests;

using BunnyTail.Resolver;
using BunnyTail.Resolver.Tests.Components;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Xunit;

// UseServiceProviderFactory による Generic Host への差し替え検証 (SPEC 9 章)
public sealed class HostIntegrationTest
{
    [Fact]
    public void HostUsesResolverServiceProvider()
    {
        using var host = new HostBuilder()
            .UseServiceProviderFactory(new ResolverServiceProviderFactory())
            .ConfigureServices(static services => services.AddComponents())
            .Build();

        Assert.IsType<ResolverServiceProvider>(host.Services);
    }

    [Fact]
    public void HostResolvesComponentsAndFrameworkServices()
    {
        using var host = new HostBuilder()
            .UseServiceProviderFactory(new ResolverServiceProviderFactory())
            .ConfigureServices(static services => services.AddComponents())
            .Build();

        // アプリコンポーネント
        var component = host.Services.GetRequiredService<TransientComponent>();
        Assert.NotNull(component.Prop);

        // フレームワークサービス (Host 内部登録が互換経路で解決できること)
        Assert.NotNull(host.Services.GetRequiredService<IHostEnvironment>());
        Assert.NotNull(host.Services.GetRequiredService<IHostApplicationLifetime>());

        // スコープ動作
        using var scope = host.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var scoped1 = scope.ServiceProvider.GetRequiredService<IScopedService>();
        var scoped2 = scope.ServiceProvider.GetRequiredService<IScopedService>();
        Assert.Same(scoped1, scoped2);
    }

    [Fact]
    public async Task HostStartsAndStops()
    {
        using var host = new HostBuilder()
            .UseServiceProviderFactory(new ResolverServiceProviderFactory())
            .ConfigureServices(static services => services.AddComponents())
            .Build();

        await host.StartAsync();
        await host.StopAsync();
    }
}
