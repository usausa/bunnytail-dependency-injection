namespace BunnyTail.DependencyInjection.CompatibilityTest;

using BunnyTail.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Specification;

public sealed class DependencyInjectionComplianceTests : DependencyInjectionSpecificationTests
{
    public override bool SupportsIServiceProviderIsService => true;

    protected override IServiceProvider CreateServiceProvider(IServiceCollection serviceCollection)
    {
        var factory = new GeneratedServiceProviderFactory();
        return factory.CreateServiceProvider(factory.CreateBuilder(serviceCollection));
    }
}
