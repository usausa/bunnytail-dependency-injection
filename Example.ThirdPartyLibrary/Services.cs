namespace Example.ThirdPartyLibrary;

using Microsoft.Extensions.DependencyInjection;

// This project stands in for a third party library. It does not reference BunnyTail.DependencyInjection,
// so none of its types carry attributes and its registrations are invisible to the application's generator.

public interface IMessageSource
{
    string GetMessage();
}

public sealed class MessageSource : IMessageSource
{
    public string GetMessage() => "third party message";
}

// Registered through the library's own extension method, which is how a third party library ships registrations
public static class MessageSourceRegistrations
{
    public static IServiceCollection AddMessageSource(this IServiceCollection services)
    {
        services.AddSingleton<IMessageSource, MessageSource>();
        return services;
    }
}

// ComponentRegistration target: a plain type registered by convention from the application side
public sealed class ExternalWorker
{
    private readonly IMessageSource source;

    public ExternalWorker(IMessageSource source)
    {
        this.source = source;
    }

    public string Describe() => $"external worker ({source.GetMessage()})";
}

// GenerateComponentFactory target: registered by the library's own extension method
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
