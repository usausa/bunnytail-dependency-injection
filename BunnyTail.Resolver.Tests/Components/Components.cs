#pragma warning disable IDE0051
namespace BunnyTail.Resolver.Tests.Components;

using BunnyTail.Resolver;

using Microsoft.Extensions.DependencyInjection;

// ユーザーコード想定のサンプルコンポーネント群 (属性ベース登録)
// Sample components representing user code (attribute based registration).

public interface IScopedService
{
    Guid Id { get; }
}

public interface IKeyedService;

[Singleton]
public sealed class PropDependency;

[Singleton]
public sealed class SingletonComponent;

[Transient]
public sealed class TransientComponent
{
    public SingletonComponent Singleton { get; }

    [Inject]
    public PropDependency Prop { get; set; } = default!;

    public TransientComponent(SingletonComponent singleton)
    {
        Singleton = singleton;
    }
}

[Scoped]
public sealed class ScopedComponent : IScopedService
{
    public Guid Id { get; } = Guid.NewGuid();
}

[Singleton(As = typeof(IKeyedService), Key = "primary")]
public sealed class PrimaryKeyedComponent : IKeyedService;

[Singleton]
public sealed class DisposableSingleton : IDisposable
{
    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}

// transient グラフ (インライン展開対象)。LeafDependency は実行時差し替えテスト (非ジェネリック登録) のため非 sealed
// Transient graph (inline expansion target). LeafDependency is unsealed for the runtime replacement test (non-generic registration).
[Transient]
public class LeafDependency;

public sealed class DerivedLeafDependency : LeafDependency;

[Transient]
public sealed class BranchA(LeafDependency leaf)
{
    public LeafDependency Leaf { get; } = leaf;
}

[Transient]
public sealed class BranchB(LeafDependency leaf)
{
    public LeafDependency Leaf { get; } = leaf;
}

[Transient]
public sealed class GraphRoot(BranchA a, BranchB b)
{
    public BranchA A { get; } = a;

    public BranchB B { get; } = b;
}

// disposable な transient はインライン展開されず、スコープの disposal 追跡を維持する
// Disposable transients are never inlined and keep the scope's disposal tracking.
[Transient]
public sealed class DisposableLeaf : IDisposable
{
    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}

// 生成 enumerable ファクトリ (全要素 transient) の検証用
// For verifying the generated enumerable factory (all-transient elements).
public interface IMultiLeaf;

[Transient(As = typeof(IMultiLeaf))]
public sealed class MultiLeafA : IMultiLeaf;

[Transient(As = typeof(IMultiLeaf))]
public sealed class MultiLeafB : IMultiLeaf;

// deps 充填の遅延性検証用 (singleton は消費側の初回解決まで生成されない)
// For verifying lazy deps filling: the singleton is not created until the consumer's first resolution.
[Singleton]
public sealed class LazyProbeSingleton
{
    private static int created;

    public static int Created => created;

    public LazyProbeSingleton()
    {
        Interlocked.Increment(ref created);
    }
}

[Transient]
public sealed class LazyProbeConsumer(LazyProbeSingleton dependency)
{
    public LazyProbeSingleton Dependency { get; } = dependency;
}

// 初期化コールバック (PostConstruct 指定 / IInitializable 実装)
// Initialization callbacks (PostConstruct specification / IInitializable implementation).
[Singleton(PostConstruct = nameof(Setup))]
public sealed class PostConstructComponent
{
    public bool Initialized { get; private set; }

    public void Setup() => Initialized = true;
}

[Transient]
public sealed class InitializableComponent : IInitializable
{
    public bool Initialized { get; private set; }

    public void Initialize() => Initialized = true;
}

// 初期化は [Inject] プロパティ注入の後に呼ばれる
// Initialization runs after [Inject] property injection.
[Transient]
public sealed class OrderedInitComponent : IInitializable
{
    [Inject]
    public PropDependency Prop { get; set; } = default!;

    public bool PropWasSetOnInitialize { get; private set; }

    // 注釈上は非 null だが、注入順序 (初期化が注入後か) を確認するための判定
    // The annotation says non-null; the check verifies the ordering, that initialization runs after injection.
    // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
    public void Initialize() => PropWasSetOnInitialize = Prop is not null;
}

// 既定値付き引数 → 生成ファクトリ不適格 → 互換経路での PostConstruct 呼び出しを検証する
// Default-valued parameter makes the factory ineligible, exercising PostConstruct on the runtime path.
[Transient(PostConstruct = nameof(Setup))]
public sealed class ReflectionInitComponent
{
    public ReflectionInitComponent(int value = 0)
    {
        _ = value;
    }

    public bool Initialized { get; private set; }

    public void Setup() => Initialized = true;
}

[Transient]
public sealed class NodeWithDisposable(DisposableLeaf leaf)
{
    public DisposableLeaf Leaf { get; } = leaf;
}

// keyed deps 形ファクトリの検証用 (singleton 依存 + [ServiceKey] 注入)
// For verifying the keyed deps-shaped factory (a singleton dependency plus [ServiceKey] injection).
[Singleton]
public sealed class KeyedProbeDependency;

public interface IKeyedWithDependency
{
    KeyedProbeDependency Probe { get; }

    string Key { get; }
}

[Transient(As = typeof(IKeyedWithDependency), Key = "kd")]
public sealed class KeyedWithDependency(KeyedProbeDependency probe, [ServiceKey] string key) : IKeyedWithDependency
{
    public KeyedProbeDependency Probe { get; } = probe;

    public string Key { get; } = key;
}

// 診断テスト用。属性も Add* 呼び出しも無いため生成ファクトリが作られない型
// For the diagnostics test: a type with neither an attribute nor an Add* call, so no factory is generated.
public interface IUntrackedProbe;

public sealed class UntrackedProbe : IUntrackedProbe;
