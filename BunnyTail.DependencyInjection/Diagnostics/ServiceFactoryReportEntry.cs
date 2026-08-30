namespace BunnyTail.DependencyInjection.Diagnostics;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;

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

    internal ServiceFactoryReportEntry(
        Type serviceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type? implementationType,
        object? serviceKey,
        ServiceLifetime lifetime,
        ServiceFactoryStatus status)
    {
        ServiceType = serviceType;
        ImplementationType = implementationType;
        ServiceKey = serviceKey;
        Lifetime = lifetime;
        Status = status;
        CanGenerateFactory = (implementationType is not null) && IsFactoryGeneratable(implementationType);
    }

    private static bool IsFactoryGeneratable([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type)
    {
        if (!IsPubliclyVisible(type) ||
            !type.IsClass ||
            type.IsAbstract ||
            type.ContainsGenericParameters)
        {
            return false;
        }

        var constructor = SelectConstructor(type);
        if (constructor is null)
        {
            return false;
        }

        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var parameter in constructor.GetParameters())
        {
            if (parameter.HasDefaultValue)
            {
                return false;
            }
        }

        return true;
    }

    private static ConstructorInfo? SelectConstructor([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type)
    {
        ConstructorInfo? selected = null;
        foreach (var constructor in type.GetConstructors())
        {
            if ((selected is null) || (constructor.GetParameters().Length > selected.GetParameters().Length))
            {
                selected = constructor;
            }
        }

        return selected;
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
