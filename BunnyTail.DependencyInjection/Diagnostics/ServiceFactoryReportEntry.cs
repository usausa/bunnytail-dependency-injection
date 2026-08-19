namespace BunnyTail.DependencyInjection.Diagnostics;

using Microsoft.Extensions.DependencyInjection;

public enum ServiceFactoryStatus
{
    Generated,
    RuntimeFallback,
    NotApplicable,
    Unresolvable
}

public sealed class ServiceFactoryReportEntry
{
    public Type ServiceType { get; }

    public Type? ImplementationType { get; }

    public object? ServiceKey { get; }

    public ServiceLifetime Lifetime { get; }

    public ServiceFactoryStatus Status { get; }

    public bool CanGenerateFactory { get; }

    internal ServiceFactoryReportEntry(Type serviceType, Type? implementationType, object? serviceKey, ServiceLifetime lifetime, ServiceFactoryStatus status)
    {
        ServiceType = serviceType;
        ImplementationType = implementationType;
        ServiceKey = serviceKey;
        Lifetime = lifetime;
        Status = status;
        CanGenerateFactory = (implementationType is not null) && IsPubliclyVisible(implementationType);
    }

    private static bool IsPubliclyVisible(Type type)
    {
        while (type.IsNested)
        {
            if (!type.IsNestedPublic || (type.DeclaringType is null))
            {
                return false;
            }

            type = type.DeclaringType;
        }

        return type.IsPublic;
    }
}
