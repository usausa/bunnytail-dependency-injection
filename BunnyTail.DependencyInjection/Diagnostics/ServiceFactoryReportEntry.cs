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

    // 生成側の受け入れ条件に合わせる。合わせないと生成できない型を候補として報告してしまう
    // Mirrors what the generator accepts. Without it the report suggests types the generator refuses.
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

        // 既定値付き引数を持つコンストラクタは生成対象外 (GetRequiredService と挙動が変わるため)
        // A constructor with defaulted parameters is not generated (behavior differs from GetRequiredService).
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

    // MEDI 規則と同じく、パラメーター数が最大の public コンストラクタ
    // The public constructor with the most parameters, the same as the MEDI rule.
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
