namespace BunnyTail.Resolver.CompatibilityTest;

using BunnyTail.Resolver;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Specification;

public sealed class DependencyInjectionComplianceTest : DependencyInjectionSpecificationTests
{
    public override bool SupportsIServiceProviderIsService => true;

    protected override IServiceProvider CreateServiceProvider(IServiceCollection serviceCollection)
    {
        var factory = new ResolverServiceProviderFactory();
        return factory.CreateServiceProvider(factory.CreateBuilder(serviceCollection));
    }
}
