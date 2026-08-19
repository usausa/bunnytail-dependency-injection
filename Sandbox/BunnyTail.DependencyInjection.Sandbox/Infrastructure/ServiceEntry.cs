namespace BunnyTail.DependencyInjection.Sandbox.Infrastructure;

// Minimal payload that mimics an entry of the service table.
// Minimal payload standing in for a service table entry.
public sealed class ServiceEntry
{
    public readonly Type ServiceType;

    public readonly int Index;

    public ServiceEntry(Type serviceType, int index)
    {
        ServiceType = serviceType;
        Index = index;
    }
}
