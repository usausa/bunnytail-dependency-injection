namespace BunnyTail.DependencyInjection.Internal;

using System.ComponentModel;

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class InlinedDependency
{
    public Type ServiceType { get; }

    public Type ImplementationType { get; }

    public InlinedDependency(Type serviceType, Type implementationType)
    {
        ServiceType = serviceType;
        ImplementationType = implementationType;
    }
}
