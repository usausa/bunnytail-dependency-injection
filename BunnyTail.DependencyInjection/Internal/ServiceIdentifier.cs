namespace BunnyTail.DependencyInjection.Internal;

using System.Runtime.CompilerServices;

internal readonly struct ServiceIdentifier : IEquatable<ServiceIdentifier>
{
    public readonly Type ServiceType;

    public readonly object? Key;

    public ServiceIdentifier(Type serviceType, object? key)
    {
        ServiceType = serviceType;
        Key = key;
    }

    public bool Equals(ServiceIdentifier other)
    {
        if (!ReferenceEquals(ServiceType, other.ServiceType))
        {
            return false;
        }

        if (Key is null)
        {
            return other.Key is null;
        }

        return other.Key is not null && Key.Equals(other.Key);
    }

    public override bool Equals(object? obj) => obj is ServiceIdentifier other && Equals(other);

    public override int GetHashCode()
    {
        var hash = RuntimeHelpers.GetHashCode(ServiceType);
        return Key is null ? hash : (hash * 397) ^ Key.GetHashCode();
    }

    public override string ToString() => Key is null ? ServiceType.ToString() : $"({ServiceType}, {Key})";
}
