namespace Example;

using BunnyTail.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;

// Convention registration through referenced assembly metadata scanning
internal static partial class ExternalRegistrations
{
    [ComponentRegistration(Lifetime.Transient, "^ExternalWorker$", Assembly = "Example.ThirdPartyLibrary")]
    public static partial IServiceCollection AddLibraryWorkers(this IServiceCollection services);
}
