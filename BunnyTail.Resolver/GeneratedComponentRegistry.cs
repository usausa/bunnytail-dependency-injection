namespace BunnyTail.Resolver;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

// 生成ファクトリがインライン展開した依存の前提。「サービス型 ServiceType の解決が、実装型 ImplementationType の
// 生成ファクトリによる transient 解決になる」ことを表明する。実行時登録がこれを満たさない場合
// (差し替え・lifetime 変更・ファクトリ登録等)、エンジンは生成ファクトリを採用せず互換経路へフォールバックする
// Assumption of a dependency inlined into a generated factory. Declares that resolving ServiceType results in
// a transient resolution through the generated factory of ImplementationType. When the runtime registrations
// no longer satisfy this (replacement, lifetime change, factory registration, ...), the engine rejects the
// generated factory and falls back to the runtime path.
public sealed class InlinedDependency
{
    public Type ServiceType { get; }

    public Type ImplementationType { get; }

    public InlinedDependency(Type serviceType, Type implementationType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(implementationType);
        ServiceType = serviceType;
        ImplementationType = implementationType;
    }
}

// 生成コードが [ModuleInitializer] から実装型→生成ファクトリを登録するレジストリ。
// エンジンは ImplementationType 登録の実現時にここを引き、「MEDI 規則で選択されたコンストラクタ = 生成時に
// 前提としたコンストラクタ」が成立する場合のみ生成ファクトリを採用する (不成立ならリフレクション経路へフォールバック)
// Registry where generated code registers implementation type -> generated factory from [ModuleInitializer].
// The engine consults it when realizing an ImplementationType registration, and adopts the generated factory
// only when the constructor selected by MEDI rules matches the one assumed at generation time
// (otherwise it falls back to the reflection path).
public static class GeneratedComponentRegistry
{
    internal sealed class Entry
    {
#pragma warning disable SA1401
        public readonly Type[] ConstructorParameterTypes;

        public readonly InlinedDependency[] InlinedDependencies;

        public readonly Func<IServiceProvider, object> Factory;
#pragma warning restore SA1401

        public Entry(Type[] constructorParameterTypes, InlinedDependency[] inlinedDependencies, Func<IServiceProvider, object> factory)
        {
            ConstructorParameterTypes = constructorParameterTypes;
            InlinedDependencies = inlinedDependencies;
            Factory = factory;
        }
    }

    internal sealed class KeyedEntry
    {
#pragma warning disable SA1401
        public readonly Type[] ConstructorParameterTypes;

        public readonly InlinedDependency[] InlinedDependencies;

        public readonly Func<IServiceProvider, object?, object> Factory;
#pragma warning restore SA1401

        public KeyedEntry(Type[] constructorParameterTypes, InlinedDependency[] inlinedDependencies, Func<IServiceProvider, object?, object> factory)
        {
            ConstructorParameterTypes = constructorParameterTypes;
            InlinedDependencies = inlinedDependencies;
            Factory = factory;
        }
    }

    private static readonly ConcurrentDictionary<Type, Entry> Map = new(IdentityTypeComparer.Instance);

    private static readonly ConcurrentDictionary<Type, KeyedEntry> KeyedMap = new(IdentityTypeComparer.Instance);

    public static void Register(Type implementationType, Type[] constructorParameterTypes, Func<IServiceProvider, object> factory) =>
        Register(implementationType, constructorParameterTypes, [], factory);

    public static void Register(Type implementationType, Type[] constructorParameterTypes, InlinedDependency[] inlinedDependencies, Func<IServiceProvider, object> factory)
    {
        ArgumentNullException.ThrowIfNull(implementationType);
        ArgumentNullException.ThrowIfNull(constructorParameterTypes);
        ArgumentNullException.ThrowIfNull(inlinedDependencies);
        ArgumentNullException.ThrowIfNull(factory);
        Map[implementationType] = new Entry(constructorParameterTypes, inlinedDependencies, factory);
    }

    public static void RegisterKeyed(Type implementationType, Type[] constructorParameterTypes, Func<IServiceProvider, object?, object> factory) =>
        RegisterKeyed(implementationType, constructorParameterTypes, [], factory);

    public static void RegisterKeyed(Type implementationType, Type[] constructorParameterTypes, InlinedDependency[] inlinedDependencies, Func<IServiceProvider, object?, object> factory)
    {
        ArgumentNullException.ThrowIfNull(implementationType);
        ArgumentNullException.ThrowIfNull(constructorParameterTypes);
        ArgumentNullException.ThrowIfNull(inlinedDependencies);
        ArgumentNullException.ThrowIfNull(factory);
        KeyedMap[implementationType] = new KeyedEntry(constructorParameterTypes, inlinedDependencies, factory);
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
