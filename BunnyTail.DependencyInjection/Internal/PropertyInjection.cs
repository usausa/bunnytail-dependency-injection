namespace BunnyTail.DependencyInjection.Internal;

using System.Reflection;

internal readonly struct PropertyInjection
{
#pragma warning disable SA1401
    public readonly PropertyInfo Property;

    public readonly ParameterPlan Plan;
#pragma warning restore SA1401

    public PropertyInjection(PropertyInfo property, ParameterPlan plan)
    {
        Property = property;
        Plan = plan;
    }
}
