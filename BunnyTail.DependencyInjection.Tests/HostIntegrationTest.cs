namespace BunnyTail.DependencyInjection.Tests;

using BunnyTail.DependencyInjection;
using BunnyTail.DependencyInjection.Tests.Components;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Xunit;

public sealed class HostIntegrationTest
{
    [Fact]
    public void HostUsesGeneratedServiceProvider()
    {
        using var host = new HostBuilder()
            .UseServiceProviderFactory(new GeneratedServiceProviderFactory())
            .ConfigureServices(static services => services.AddGeneratedComponents())
            .Build();

        Assert.IsType<GeneratedServiceProvider>(host.Services);
    }

    [Fact]
    public void HostResolvesComponentsAndFrameworkServices()
    {
        using var host = new HostBuilder()
            .UseServiceProviderFactory(new GeneratedServiceProviderFactory())
            .ConfigureServices(static services => services.AddGeneratedComponents())
            .Build();

        // Application component
        var component = host.Services.GetRequiredService<TransientComponent>();
        Assert.NotNull(component.Prop);

        // Framework services
        Assert.NotNull(host.Services.GetRequiredService<IHostEnvironment>());
        Assert.NotNull(host.Services.GetRequiredService<IHostApplicationLifetime>());

        // Scope behavior
        using var scope = host.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var scoped1 = scope.ServiceProvider.GetRequiredService<IScopedService>();
        var scoped2 = scope.ServiceProvider.GetRequiredService<IScopedService>();
        Assert.Same(scoped1, scoped2);
    }

    [Fact]
    public async Task HostStartsAndStops()
    {
        using var host = new HostBuilder()
            .UseServiceProviderFactory(new GeneratedServiceProviderFactory())
            .ConfigureServices(static services => services.AddGeneratedComponents())
            .Build();

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }
}
