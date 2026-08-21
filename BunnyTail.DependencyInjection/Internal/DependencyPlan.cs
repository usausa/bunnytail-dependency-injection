namespace BunnyTail.DependencyInjection.Internal;

using System.ComponentModel;

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class DependencyPlan
{
    public Type ServiceType { get; }

    public Type? ImplementationType { get; }

    public bool UseAccessor => ImplementationType is null;

    public DependencyPlan(Type serviceType)
    {
        ServiceType = serviceType;
    }

    public DependencyPlan(Type serviceType, Type implementationType)
    {
        ServiceType = serviceType;
        ImplementationType = implementationType;
    }
}
