namespace BunnyTail.DependencyInjection.Generator.Models;

internal static class DependencyKinds
{
    public const int Service = 0;
    // [ServiceKey]
    public const int ServiceKey = 1;
    // [FromKeyedServices(key)]
    public const int KeyedExplicit = 2;
    // [FromKeyedServices]
    public const int KeyedInherit = 3;
}
