namespace BunnyTail.Resolver.Tests.Components;

using BunnyTail.Resolver;

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

[Transient]
public sealed class NodeWithDisposable(DisposableLeaf leaf)
{
    public DisposableLeaf Leaf { get; } = leaf;
}
