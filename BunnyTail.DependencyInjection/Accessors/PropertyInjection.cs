namespace BunnyTail.DependencyInjection.Accessors;

using System.Reflection;

internal readonly struct PropertyInjection
{
#pragma warning disable SA1401
    public readonly MethodInvoker Setter;

    public readonly ParameterPlan Plan;
#pragma warning restore SA1401

    public PropertyInjection(MethodInvoker setter, ParameterPlan plan)
    {
        Setter = setter;
        Plan = plan;
    }
}
