#pragma warning disable IDE0051
namespace BunnyTail.DependencyInjection.Tests.Components;

using BunnyTail.DependencyInjection;

using Microsoft.Extensions.DependencyInjection;

// Sample

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

[Scoped(WithInterfaces = true)]
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

// Transient graph

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

// Disposable

[Transient]
public sealed class DisposableLeaf : IDisposable
{
    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}

// Multi-leaf

public interface IMultiLeaf;

[Transient(As = typeof(IMultiLeaf))]
public sealed class MultiLeafA : IMultiLeaf;

[Transient(As = typeof(IMultiLeaf))]
public sealed class MultiLeafB : IMultiLeaf;

// Lazy dependency filling

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

// Initialization callbacks

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

// Property injection

[Transient]
public sealed class OrderedInitComponent : IInitializable
{
    [Inject]
    public PropDependency Prop { get; set; } = default!;

    public bool PropWasSetOnInitialize { get; private set; }

    // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
    public void Initialize() => PropWasSetOnInitialize = Prop is not null;
}

// Default value parameter

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

// Keyed dependency filling

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

// Keyed dependency injection

public interface IKeyedLeaf
{
    string Name { get; }
}

[Singleton(As = typeof(IKeyedLeaf), Key = "left")]
public sealed class LeftKeyedLeaf : IKeyedLeaf
{
    public string Name => "left";
}

[Singleton(As = typeof(IKeyedLeaf), Key = "right")]
public sealed class RightKeyedLeaf : IKeyedLeaf
{
    public string Name => "right";
}

// 非 keyed 登録 + キー指定の依存。コンストラクタ引数は [FromKeyedServices]、プロパティは [Inject(Key)]
// Non-keyed registration with keyed dependencies: [FromKeyedServices] on the parameter, [Inject(Key)] on the property.
[Transient]
public sealed class KeyedConsumer([FromKeyedServices("left")] IKeyedLeaf left)
{
    public IKeyedLeaf Left { get; } = left;

    [Inject(Key = "right")]
    public IKeyedLeaf Right { get; set; } = default!;
}

// keyed 登録でも [Inject(Key)] は独立したキーを指す
// Even on a keyed registration, [Inject(Key)] names its own key.
public interface IKeyedPropertyConsumer
{
    IKeyedLeaf Leaf { get; }
}

[Transient(As = typeof(IKeyedPropertyConsumer), Key = "left")]
public sealed class KeyedPropertyConsumer : IKeyedPropertyConsumer
{
    [Inject(Key = "right")]
    public IKeyedLeaf Leaf { get; set; } = default!;
}

// Add* calls

public interface IUntrackedProbe;

public sealed class UntrackedProbe : IUntrackedProbe;

// Convention based registration

public interface IBarService;

public interface IMixed1;

public interface IMixed2;

public sealed class EchoService;

public sealed class BarService : IBarService;

public sealed class MixedService : IMixed1, IMixed2;

// Second registration

public sealed class SampleRepository;

// Private registration

public sealed class SampleGadget;

// DependencyInjectionIgnoreAttribute

public interface IIgnoredMarker;

public sealed class IgnoredMarkerService : IIgnoredMarker;
