namespace BunnyTail.DependencyInjection.Accessors;

internal enum ResultCache
{
    // Transient: Created every
    None,
    // Singleton: Held by root scope
    Root,
    // Scoped: Held by resolving scope
    Scoped
}
