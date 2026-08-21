namespace BunnyTail.DependencyInjection.Internal;

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;

[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedFactoryRegistry
{
    //--------------------------------------------------------------------------------
    // Entry
    //--------------------------------------------------------------------------------

    internal sealed class Entry
    {
#pragma warning disable SA1401
        public readonly Type[] ConstructorParameterTypes;

        public readonly InlinedDependency[] InlinedDependencies;

        public readonly DependencyPlan[] Dependencies;

        public readonly Func<IServiceProvider, object>? Factory;

        public readonly Func<IServiceProvider, object?[], object>? DependencyFactory;
#pragma warning restore SA1401

        public Entry(Type[] constructorParameterTypes, InlinedDependency[] inlinedDependencies, Func<IServiceProvider, object> factory)
        {
            ConstructorParameterTypes = constructorParameterTypes;
            InlinedDependencies = inlinedDependencies;
            Dependencies = [];
            Factory = factory;
        }

        public Entry(Type[] constructorParameterTypes, InlinedDependency[] inlinedDependencies, DependencyPlan[] dependencies, Func<IServiceProvider, object?[], object> dependencyFactory)
        {
            ConstructorParameterTypes = constructorParameterTypes;
            InlinedDependencies = inlinedDependencies;
            Dependencies = dependencies;
            DependencyFactory = dependencyFactory;
        }
    }

    internal sealed class KeyedEntry
    {
#pragma warning disable SA1401
        public readonly Type[] ConstructorParameterTypes;

        public readonly InlinedDependency[] InlinedDependencies;

        public readonly DependencyPlan[] Dependencies;

        public readonly Func<IServiceProvider, object?, object>? Factory;

        public readonly Func<IServiceProvider, object?, object?[], object>? KeyedDependencyFactory;
#pragma warning restore SA1401

        public KeyedEntry(Type[] constructorParameterTypes, InlinedDependency[] inlinedDependencies, Func<IServiceProvider, object?, object> factory)
        {
            ConstructorParameterTypes = constructorParameterTypes;
            InlinedDependencies = inlinedDependencies;
            Dependencies = [];
            Factory = factory;
        }

        public KeyedEntry(Type[] constructorParameterTypes, InlinedDependency[] inlinedDependencies, DependencyPlan[] dependencies, Func<IServiceProvider, object?, object?[], object> keyedDependencyFactory)
        {
            ConstructorParameterTypes = constructorParameterTypes;
            InlinedDependencies = inlinedDependencies;
            Dependencies = dependencies;
            KeyedDependencyFactory = keyedDependencyFactory;
        }
    }

    internal sealed class EnumerableEntry
    {
#pragma warning disable SA1401
        public readonly Type[] ElementImplementationTypes;

        public readonly Func<IServiceProvider, object> Factory;
#pragma warning restore SA1401

        public EnumerableEntry(Type[] elementImplementationTypes, Func<IServiceProvider, object> factory)
        {
            ElementImplementationTypes = elementImplementationTypes;
            Factory = factory;
        }
    }

    //--------------------------------------------------------------------------------
    // Registration from generated code
    //--------------------------------------------------------------------------------

    private static readonly ConcurrentDictionary<Type, Entry> Map = new(IdentityTypeComparer.Instance);

    private static readonly ConcurrentDictionary<Type, KeyedEntry> KeyedMap = new(IdentityTypeComparer.Instance);

    private static readonly ConcurrentDictionary<Type, EnumerableEntry> EnumerableMap = new(IdentityTypeComparer.Instance);

    private static readonly ConcurrentDictionary<Type, string> InitializerMap = new(IdentityTypeComparer.Instance);

    public static void Register(Type implementationType, Type[] constructorParameterTypes, Func<IServiceProvider, object> factory) =>
        Register(implementationType, constructorParameterTypes, [], factory);

    public static void Register(Type implementationType, Type[] constructorParameterTypes, InlinedDependency[] inlinedDependencies, Func<IServiceProvider, object> factory)
    {
        Map[implementationType] = new Entry(constructorParameterTypes, inlinedDependencies, factory);
    }

    public static void Register(Type implementationType, Type[] constructorParameterTypes, InlinedDependency[] inlinedDependencies, DependencyPlan[] dependencies, Func<IServiceProvider, object?[], object> factory)
    {
        Map[implementationType] = new Entry(constructorParameterTypes, inlinedDependencies, dependencies, factory);
    }

    public static void RegisterKeyed(Type implementationType, Type[] constructorParameterTypes, Func<IServiceProvider, object?, object> factory) =>
        RegisterKeyed(implementationType, constructorParameterTypes, [], factory);

    public static void RegisterKeyed(Type implementationType, Type[] constructorParameterTypes, InlinedDependency[] inlinedDependencies, Func<IServiceProvider, object?, object> factory)
    {
        KeyedMap[implementationType] = new KeyedEntry(constructorParameterTypes, inlinedDependencies, factory);
    }

    public static void RegisterKeyed(Type implementationType, Type[] constructorParameterTypes, InlinedDependency[] inlinedDependencies, DependencyPlan[] dependencies, Func<IServiceProvider, object?, object?[], object> factory)
    {
        KeyedMap[implementationType] = new KeyedEntry(constructorParameterTypes, inlinedDependencies, dependencies, factory);
    }

    public static void RegisterEnumerable(Type elementType, Type[] elementImplementationTypes, Func<IServiceProvider, object> factory)
    {
        EnumerableMap[elementType] = new EnumerableEntry(elementImplementationTypes, factory);
    }

    public static void RegisterInitializer(Type implementationType, string postConstructMethodName)
    {
        InitializerMap[implementationType] = postConstructMethodName;
    }

    //--------------------------------------------------------------------------------
    // Lookup from engine
    //--------------------------------------------------------------------------------

    internal static bool TryGet(Type implementationType, out Entry entry) => Map.TryGetValue(implementationType, out entry!);

    internal static bool TryGetInitializer(Type implementationType, out string postConstructMethodName) =>
        InitializerMap.TryGetValue(implementationType, out postConstructMethodName!);

    internal static bool TryGetEnumerable(Type elementType, out EnumerableEntry entry) => EnumerableMap.TryGetValue(elementType, out entry!);

    internal static bool TryGetKeyed(Type implementationType, out KeyedEntry entry) => KeyedMap.TryGetValue(implementationType, out entry!);

    private sealed class IdentityTypeComparer : IEqualityComparer<Type>
    {
        public static readonly IdentityTypeComparer Instance = new();

        public bool Equals(Type? x, Type? y) => ReferenceEquals(x, y);

        public int GetHashCode(Type obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
