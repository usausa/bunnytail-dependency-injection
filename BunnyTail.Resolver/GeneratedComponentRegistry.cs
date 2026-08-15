namespace BunnyTail.Resolver;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

// 生成コードが [ModuleInitializer] から実装型→生成ファクトリを登録するレジストリ (SPEC 2.3)。
// エンジンは ImplementationType 登録の実現時にここを引き、
// 「MEDI 規則で選択されたコンストラクタ = 生成時に前提としたコンストラクタ」が成立する場合のみ
// 生成ファクトリを採用する (不成立ならリフレクション経路へフォールバック)
public static class GeneratedComponentRegistry
{
    internal sealed class Entry
    {
#pragma warning disable SA1401
        public readonly Type[] ConstructorParameterTypes;

        public readonly Func<IServiceProvider, object> Factory;
#pragma warning restore SA1401

        public Entry(Type[] constructorParameterTypes, Func<IServiceProvider, object> factory)
        {
            ConstructorParameterTypes = constructorParameterTypes;
            Factory = factory;
        }
    }

    internal sealed class KeyedEntry
    {
#pragma warning disable SA1401
        public readonly Type[] ConstructorParameterTypes;

        public readonly Func<IServiceProvider, object?, object> Factory;
#pragma warning restore SA1401

        public KeyedEntry(Type[] constructorParameterTypes, Func<IServiceProvider, object?, object> factory)
        {
            ConstructorParameterTypes = constructorParameterTypes;
            Factory = factory;
        }
    }

    private static readonly ConcurrentDictionary<Type, Entry> Map = new(IdentityTypeComparer.Instance);

    private static readonly ConcurrentDictionary<Type, KeyedEntry> KeyedMap = new(IdentityTypeComparer.Instance);

    public static void Register(Type implementationType, Type[] constructorParameterTypes, Func<IServiceProvider, object> factory)
    {
        ArgumentNullException.ThrowIfNull(implementationType);
        ArgumentNullException.ThrowIfNull(constructorParameterTypes);
        ArgumentNullException.ThrowIfNull(factory);
        Map[implementationType] = new Entry(constructorParameterTypes, factory);
    }

    public static void RegisterKeyed(Type implementationType, Type[] constructorParameterTypes, Func<IServiceProvider, object?, object> factory)
    {
        ArgumentNullException.ThrowIfNull(implementationType);
        ArgumentNullException.ThrowIfNull(constructorParameterTypes);
        ArgumentNullException.ThrowIfNull(factory);
        KeyedMap[implementationType] = new KeyedEntry(constructorParameterTypes, factory);
    }

    internal static bool TryGet(Type implementationType, out Entry entry) => Map.TryGetValue(implementationType, out entry!);

    internal static bool TryGetKeyed(Type implementationType, out KeyedEntry entry) => KeyedMap.TryGetValue(implementationType, out entry!);

    private sealed class IdentityTypeComparer : IEqualityComparer<Type>
    {
        public static readonly IdentityTypeComparer Instance = new();

        public bool Equals(Type? x, Type? y) => ReferenceEquals(x, y);

        public int GetHashCode(Type obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
