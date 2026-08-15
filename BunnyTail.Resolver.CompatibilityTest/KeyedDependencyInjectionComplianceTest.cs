namespace BunnyTail.Resolver.CompatibilityTest;

using BunnyTail.Resolver;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Specification;

public sealed class KeyedDependencyInjectionComplianceTest : KeyedDependencyInjectionSpecificationTests
{
    public override bool SupportsIServiceProviderIsKeyedService => true;

    protected override IServiceProvider CreateServiceProvider(IServiceCollection collection)
    {
        var factory = new ResolverServiceProviderFactory();
        return factory.CreateServiceProvider(factory.CreateBuilder(collection));
    }
}
