// ComponentModule attribute is used to mark a module type that provides component registrations for dependency injection
[assembly: BunnyTail.DependencyInjection.ComponentModule(typeof(Example.Library2.LibraryModule))]

namespace Example.Library2;

using Microsoft.Extensions.DependencyInjection;

public interface IMessageSource
{
    string GetMessage();
}

public sealed class MessageSource : IMessageSource
{
    public string GetMessage() => "manual module";
}

public static class LibraryModule
{
    public static IServiceCollection AddGeneratedComponents(this IServiceCollection services)
    {
        services.AddSingleton<IMessageSource, MessageSource>();
        return services;
    }
}

// ComponentRegistration target
public sealed class ExternalWorker
{
    private readonly IMessageSource source;

    public ExternalWorker(IMessageSource source)
    {
        this.source = source;
    }

    public string Describe() => $"external worker ({source.GetMessage()})";
}

// Library2 module registration through a conventional extension method
public sealed class ReportedService
{
    private readonly IMessageSource source;

    public ReportedService(IMessageSource source)
    {
        this.source = source;
    }

    public bool Prepared { get; private set; }

    public void Prepare() => Prepared = true;

    public string Describe() => $"reported service ({source.GetMessage()})";
}

public static class ReportedServiceRegistrations
{
    public static IServiceCollection AddReportedService(this IServiceCollection services)
    {
        services.AddTransient<ReportedService>();
        return services;
    }
}
