namespace BunnyTail.DependencyInjection.Tests;

using BunnyTail.DependencyInjection;
using BunnyTail.DependencyInjection.Tests.Components;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

public sealed class HostIntegrationTests
{
    [Fact]
    public void HostUsesGeneratedServiceProvider()
    {
        // Arrange
        using var host = new HostBuilder()
            .UseServiceProviderFactory(new GeneratedServiceProviderFactory())
            .ConfigureServices(static services => services.AddGeneratedComponents())
            .Build();

        // Act & Assert
        Assert.IsType<GeneratedServiceProvider>(host.Services);
    }

    [Fact]
    public void HostResolvesComponentsAndFrameworkServices()
    {
        // Arrange
        using var host = new HostBuilder()
            .UseServiceProviderFactory(new GeneratedServiceProviderFactory())
            .ConfigureServices(static services => services.AddGeneratedComponents())
            .Build();

        // Act & Assert
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
        // Arrange
        using var host = new HostBuilder()
            .UseServiceProviderFactory(new GeneratedServiceProviderFactory())
            .ConfigureServices(static services => services.AddGeneratedComponents())
            .Build();

        // Act
        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }
}
