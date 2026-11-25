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
        ServiceFactoryStatus status,
        ConstructorInfo? runtimeConstructor)
    {
        ServiceType = serviceType;
        ImplementationType = implementationType;
        ServiceKey = serviceKey;
        Lifetime = lifetime;
        Status = status;
        CanGenerateFactory = (implementationType is not null) && (serviceKey is null) && IsFactoryGeneratable(implementationType, runtimeConstructor);
    }

    private static bool IsFactoryGeneratable([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type, ConstructorInfo? runtimeConstructor)
    {
        if (!type.IsVisible ||
            !type.IsClass ||
            type.IsAbstract ||
            type.ContainsGenericParameters)
        {
            return false;
        }

        var constructor = SelectConstructor(type);
        if ((constructor is null) ||
            ((runtimeConstructor is not null) && (runtimeConstructor != constructor)) ||
            (constructor.GetCustomAttribute<ObsoleteAttribute>() is { IsError: true }))
        {
            return false;
        }

        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var parameter in constructor.GetParameters())
        {
            if (parameter.HasDefaultValue ||
                (parameter.GetCustomAttribute<ServiceKeyAttribute>() is not null) ||
                (parameter.GetCustomAttribute<FromKeyedServicesAttribute>() is { LookupMode: ServiceKeyLookupMode.InheritKey }))
            {
                return false;
            }
        }

        return true;
    }

    private static ConstructorInfo? SelectConstructor([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type)
    {
        ConstructorInfo? selected = null;
        ConstructorInfo? second = null;
        foreach (var constructor in type.GetConstructors())
        {
            var count = constructor.GetParameters().Length;
            if ((selected is null) || (count > selected.GetParameters().Length))
            {
                selected = constructor;
                second = null;
            }
            else if ((second is null) && (count == selected.GetParameters().Length))
            {
                second = constructor;
            }
        }

        if ((selected is not null) && (second is not null) && !HasSameParameterTypes(selected, second))
        {
            return null;
        }

        return selected;
    }

    private static bool HasSameParameterTypes(ConstructorInfo x, ConstructorInfo y) =>
        new HashSet<Type>(x.GetParameters().Select(static p => p.ParameterType)).SetEquals(y.GetParameters().Select(static p => p.ParameterType));
}
