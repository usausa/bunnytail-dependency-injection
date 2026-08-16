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

        // singleton 依存の前提。deps 配列のスロット順に対応する (DepsFactory 形のみ)
        // Singleton dependency assumptions, in deps array slot order (deps-shaped factories only).
        public readonly InlinedDependency[] SingletonDependencies;

        public readonly Func<IServiceProvider, object>? Factory;

        // singleton 依存を解決済み配列で受け取る形 (Factory と排他)
        // Shape receiving resolved singleton dependencies as an array (mutually exclusive with Factory).
        public readonly Func<IServiceProvider, object?[], object>? DepsFactory;
#pragma warning restore SA1401

        public Entry(Type[] constructorParameterTypes, InlinedDependency[] inlinedDependencies, Func<IServiceProvider, object> factory)
        {
            ConstructorParameterTypes = constructorParameterTypes;
            InlinedDependencies = inlinedDependencies;
            SingletonDependencies = [];
            Factory = factory;
        }

        public Entry(Type[] constructorParameterTypes, InlinedDependency[] inlinedDependencies, InlinedDependency[] singletonDependencies, Func<IServiceProvider, object?[], object> depsFactory)
        {
            ConstructorParameterTypes = constructorParameterTypes;
            InlinedDependencies = inlinedDependencies;
            SingletonDependencies = singletonDependencies;
            DepsFactory = depsFactory;
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

    // 生成 enumerable ファクトリ (要素型 → 全要素 transient の配列リテラル実体化)
    // Generated enumerable factories (element type -> array literal materialization of all-transient elements).
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

    private static readonly ConcurrentDictionary<Type, Entry> Map = new(IdentityTypeComparer.Instance);

    private static readonly ConcurrentDictionary<Type, KeyedEntry> KeyedMap = new(IdentityTypeComparer.Instance);

    private static readonly ConcurrentDictionary<Type, EnumerableEntry> EnumerableMap = new(IdentityTypeComparer.Instance);

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

    // singleton 依存を deps 配列で受け取る形。singletonDependencies がスロット順の前提になる
    // Deps-shaped registration receiving resolved singleton dependencies; singletonDependencies define the slot order assumptions.
    public static void Register(Type implementationType, Type[] constructorParameterTypes, InlinedDependency[] inlinedDependencies, InlinedDependency[] singletonDependencies, Func<IServiceProvider, object?[], object> factory)
    {
        ArgumentNullException.ThrowIfNull(implementationType);
        ArgumentNullException.ThrowIfNull(constructorParameterTypes);
        ArgumentNullException.ThrowIfNull(inlinedDependencies);
        ArgumentNullException.ThrowIfNull(singletonDependencies);
        ArgumentNullException.ThrowIfNull(factory);
        Map[implementationType] = new Entry(constructorParameterTypes, inlinedDependencies, singletonDependencies, factory);
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

    // 全要素 transient の IEnumerable<T> 実体化を配列リテラルへ畳む形。elementImplementationTypes が登録順の前提になる
    // Materializes an all-transient IEnumerable<T> as an array literal; elementImplementationTypes define the ordered assumptions.
    public static void RegisterEnumerable(Type elementType, Type[] elementImplementationTypes, Func<IServiceProvider, object> factory)
    {
        ArgumentNullException.ThrowIfNull(elementType);
        ArgumentNullException.ThrowIfNull(elementImplementationTypes);
        ArgumentNullException.ThrowIfNull(factory);
        EnumerableMap[elementType] = new EnumerableEntry(elementImplementationTypes, factory);
    }

    internal static bool TryGet(Type implementationType, out Entry entry) => Map.TryGetValue(implementationType, out entry!);

    internal static bool TryGetEnumerable(Type elementType, out EnumerableEntry entry) => EnumerableMap.TryGetValue(elementType, out entry!);

    internal static bool TryGetKeyed(Type implementationType, out KeyedEntry entry) => KeyedMap.TryGetValue(implementationType, out entry!);

    private sealed class IdentityTypeComparer : IEqualityComparer<Type>
    {
        public static readonly IdentityTypeComparer Instance = new();

        public bool Equals(Type? x, Type? y) => ReferenceEquals(x, y);

        public int GetHashCode(Type obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
